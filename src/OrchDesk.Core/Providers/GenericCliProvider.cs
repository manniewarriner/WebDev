using System.Runtime.CompilerServices;
using OrchDesk.Core.Models;

namespace OrchDesk.Core.Providers;

/// <summary>
/// Any agent CLI that takes a prompt on stdin and prints its answer: `codex exec -` or `opencode run`.
/// Output lines are streamed as text; the CLI runs its own tools inside the working directory.
/// </summary>
public sealed class GenericCliProvider(
    string id, string displayName, string executable, IReadOnlyList<string> args,
    IReadOnlyDictionary<string, string?>? env = null) : IModelProvider
{
    /// <summary>OpenCode 2.x ordered permissions (last match wins): deny everything, then allow read-only tools.</summary>
    public const string OpenCodeReadOnlyConfig =
        "{\"permissions\":[{\"action\":\"*\",\"resource\":\"*\",\"effect\":\"deny\"}," +
        "{\"action\":\"read\",\"resource\":\"*\",\"effect\":\"allow\"},{\"action\":\"glob\",\"resource\":\"*\",\"effect\":\"allow\"}," +
        "{\"action\":\"grep\",\"resource\":\"*\",\"effect\":\"allow\"},{\"action\":\"list\",\"resource\":\"*\",\"effect\":\"allow\"}," +
        "{\"action\":\"external_directory\",\"resource\":\"*\",\"effect\":\"deny\"}]}";

    public string ModelId => args.SkipWhile(a => a != "--model").Skip(1).FirstOrDefault() ?? "default";
    public string Id { get; } = id;
    public string DisplayName { get; } = displayName;
    public bool IsAgentic => true;
    public bool IsLocal => false;

    public Task<ProviderHealth> CheckHealthAsync(CancellationToken ct) =>
        Task.FromResult(CliRunner.FindOnPath(executable) is null ? ProviderHealth.NotConfigured : ProviderHealth.Online);

    private static bool IsOpenCode(string executable) =>
        Path.GetFileNameWithoutExtension(executable).Equals("opencode", StringComparison.OrdinalIgnoreCase);

    /// <summary>Final CLI arguments and environment for one run (read-only / host-managed runs lose write access).</summary>
    public static (List<string> Args, Dictionary<string, string?> Env) PrepareRun(
        string executable, IReadOnlyList<string> args, IReadOnlyDictionary<string, string?>? env, bool restricted)
    {
        var runArgs = args.ToList();
        var runEnv = env is null ? new Dictionary<string, string?>() : new Dictionary<string, string?>(env);
        var openCode = IsOpenCode(executable);
        if (restricted) {
            if (openCode) {
                runEnv["OPENCODE_PERMISSION"] = "{\"*\":\"deny\"}"; // 1.x only; 2.x ignores it
                runEnv["OPENCODE_CONFIG_CONTENT"] = OpenCodeReadOnlyConfig;
            } else {
                var sandbox = runArgs.IndexOf("--sandbox");
                if (sandbox >= 0 && sandbox + 1 < runArgs.Count) runArgs[sandbox + 1] = "read-only";
            }
        }
        if (openCode) {
            // 2.x: --standalone gives this run a private server so per-run env/config applies and nothing lingers.
            if (!runArgs.Contains("--standalone")) runArgs.Add("--standalone");
            runArgs.AddRange(["--format", "json"]);
        }
        return (runArgs, runEnv);
    }

    public async IAsyncEnumerable<ChatEvent> StreamChatAsync(ChatRequest request, [EnumeratorCancellation] CancellationToken ct)
    {
        var exe = CliRunner.FindOnPath(executable);
        if (exe is null)
        {
            yield return new ProviderUnavailable(Id, $"'{executable}' was not found on PATH.");
            yield break;
        }

        var openCode = IsOpenCode(executable);
        var (runArgs, runEnv) = PrepareRun(executable, args, env, request.ReadOnly || request.HostManagedTools);
        var psi = CliRunner.CreateStartInfo(exe, runArgs, request.WorkingDirectory, runEnv);
        var stderr = new List<string>();
        var any = false;
        var sawFatal = false;
        var text = new System.Text.StringBuilder();
        await foreach (var line in CliRunner.StreamAsync(psi, Transcript.Flatten(request.Messages) + (request.HostManagedTools ? HostToolProtocol.Instruction(request) : ""), ct))
        {
            if (line.IsStdErr) { stderr.Add(line.Text); continue; }
            any = true;
            if (openCode) {
                foreach (var ev in OpenCodeStreamParser.Parse(line.Text, Id)) {
                    if (ev is ProviderUnavailable or UsageLimitHit or ErrorEvent) sawFatal = true;
                    if (request.HostManagedTools && ev is TextDelta delta) text.Append(delta.Text);
                    else yield return ev;
                }
            } else if (request.HostManagedTools) text.AppendLine(line.Text);
            else yield return new TextDelta(line.Text + "\n");
        }
        if (text.Length > 0) foreach (var ev in SseChatParser.ParseContent(text.ToString())) yield return ev;

        var err = string.Join("\n", stderr.TakeLast(20));
        if (sawFatal) yield break; // the parsed error event already ended the turn
        if (ClaudeStreamParser.LooksLikeUsageLimit(err)) yield return new UsageLimitHit(Id, err);
        else if (!any && err.Length > 0) yield return new ErrorEvent(err);
        else yield return new Done();
    }
}
