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
    public string ModelId => args.SkipWhile(a => a != "--model").Skip(1).FirstOrDefault() ?? "default";
    public string Id { get; } = id;
    public string DisplayName { get; } = displayName;
    public bool IsAgentic => true;
    public bool IsLocal => false;

    public Task<ProviderHealth> CheckHealthAsync(CancellationToken ct) =>
        Task.FromResult(CliRunner.FindOnPath(executable) is null ? ProviderHealth.NotConfigured : ProviderHealth.Online);

    public async IAsyncEnumerable<ChatEvent> StreamChatAsync(ChatRequest request, [EnumeratorCancellation] CancellationToken ct)
    {
        var exe = CliRunner.FindOnPath(executable);
        if (exe is null)
        {
            yield return new ProviderUnavailable(Id, $"'{executable}' was not found on PATH.");
            yield break;
        }

        var psi = CliRunner.CreateStartInfo(exe, args, request.WorkingDirectory, env);
        var stderr = new List<string>();
        var any = false;
        await foreach (var line in CliRunner.StreamAsync(psi, Transcript.Flatten(request.Messages), ct))
        {
            if (line.IsStdErr) { stderr.Add(line.Text); continue; }
            any = true;
            yield return new TextDelta(line.Text + "\n");
        }

        var err = string.Join("\n", stderr.TakeLast(20));
        if (ClaudeStreamParser.LooksLikeUsageLimit(err)) yield return new UsageLimitHit(Id, err);
        else if (!any && err.Length > 0) yield return new ErrorEvent(err);
        else yield return new Done();
    }
}
