using System.Runtime.CompilerServices;
using OrchDesk.Core.Models;

namespace OrchDesk.Core.Providers;

public sealed record ClaudeCliOptions(
    string ConfigDir,
    string Executable = "claude",
    string? Model = null,
    string PermissionMode = "acceptEdits",
    IReadOnlyList<string>? AllowedTools = null);

/// <summary>
/// Runs the Claude Code CLI on the user's subscription. Each profile (Outlook, Gmail) is a separate
/// CLAUDE_CONFIG_DIR that was logged in once with `claude /login`. Every paid-API environment variable is
/// stripped from the child process so it can never fall back to API credits.
/// </summary>
public sealed class ClaudeCliProvider(string id, string displayName, ClaudeCliOptions options) : IModelProvider
{
    // Claude keeps its own conversation state; we remember its session id per folder and --resume it.
    private readonly Dictionary<string, string> _sessions = new(StringComparer.OrdinalIgnoreCase);

    public string Id { get; } = id;
    public string DisplayName { get; } = displayName;
    public bool IsAgentic => true;
    public bool IsLocal => false;

    public Task<ProviderHealth> CheckHealthAsync(CancellationToken ct)
    {
        if (CliRunner.FindOnPath(options.Executable) is null) return Task.FromResult(ProviderHealth.NotConfigured);
        if (!Directory.Exists(options.ConfigDir)) return Task.FromResult(ProviderHealth.NotConfigured);
        return Task.FromResult(ProviderHealth.Online);
    }

    public void ResetSession(string workingDirectory) => _sessions.Remove(workingDirectory);

    public IReadOnlyList<string> BuildArgs(ChatRequest request, string? resumeSessionId)
    {
        var args = new List<string> { "-p", "--output-format", "stream-json", "--verbose" };
        args.AddRange(["--permission-mode", request.ReadOnly ? "plan" : options.PermissionMode]);
        if (!string.IsNullOrEmpty(options.Model)) args.AddRange(["--model", options.Model]);
        if (options.AllowedTools is { Count: > 0 }) args.AddRange(["--allowedTools", string.Join(",", options.AllowedTools)]);
        if (resumeSessionId is not null) args.AddRange(["--resume", resumeSessionId]);
        return args;
    }

    public IReadOnlyDictionary<string, string?> BuildEnvironment()
    {
        var env = new Dictionary<string, string?> { ["CLAUDE_CONFIG_DIR"] = options.ConfigDir };
        foreach (var v in CliRunner.PaidApiEnvVars) env[v] = null; // null = remove
        return env;
    }

    public async IAsyncEnumerable<ChatEvent> StreamChatAsync(ChatRequest request, [EnumeratorCancellation] CancellationToken ct)
    {
        var exe = CliRunner.FindOnPath(options.Executable);
        if (exe is null)
        {
            yield return new ProviderUnavailable(Id, $"'{options.Executable}' was not found on PATH.");
            yield break;
        }

        var cwd = request.WorkingDirectory ?? Environment.CurrentDirectory;
        _sessions.TryGetValue(cwd, out var sessionId);
        // With a live session only the newest user message is sent; otherwise the whole transcript,
        // so a conversation that rotated here from another model keeps its context.
        var prompt = sessionId is not null ? Transcript.LastUserMessage(request.Messages) : Transcript.Flatten(request.Messages);

        var psi = CliRunner.CreateStartInfo(exe, BuildArgs(request, sessionId), cwd, BuildEnvironment());
        var sawResult = false;
        var stderr = new List<string>();

        await foreach (var line in CliRunner.StreamAsync(psi, prompt, ct))
        {
            if (line.IsStdErr) { stderr.Add(line.Text); continue; }
            var parsed = ClaudeStreamParser.Parse(line.Text, Id);
            if (parsed.SessionId is not null) _sessions[cwd] = parsed.SessionId;
            foreach (var ev in parsed.Events)
            {
                if (ev is Done or ErrorEvent or UsageLimitHit) sawResult = true;
                yield return ev;
            }
        }

        if (!sawResult)
        {
            var err = string.Join("\n", stderr.TakeLast(20));
            if (ClaudeStreamParser.LooksLikeUsageLimit(err)) yield return new UsageLimitHit(Id, err);
            else yield return new ErrorEvent(string.IsNullOrWhiteSpace(err) ? "Claude CLI exited without a result." : err);
        }
    }
}
