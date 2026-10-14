using System.Text.RegularExpressions;
using OrchDesk.Core.Providers;
using OrchDesk.Core.Tools;

namespace OrchDesk.Core.Overnight;

/// <summary>
/// Last check before anything is pushed. Uses gitleaks if it's installed; otherwise a built-in scan of the
/// added lines in the branch diff for common key formats.
/// </summary>
public static partial class SecretScanner
{
    public sealed record ScanResult(bool Clean, string Details);

    public static async Task<ScanResult> ScanBranchAsync(string repo, string baseRef, CancellationToken ct)
    {
        if (CliRunner.FindOnPath("gitleaks") is { } gitleaks)
        {
            var r = await ProcessRunner.RunAsync(
                ProcessRunner.Exe(gitleaks, ["git", "--no-banner", "--redact", $"--log-opts={baseRef}..HEAD", "."], repo),
                TimeSpan.FromMinutes(5), ct);
            return new ScanResult(r.ExitCode == 0, "gitleaks: " + Tail(r.Output));
        }

        var diff = await ProcessRunner.RunAsync(ProcessRunner.Exe("git", ["--no-pager", "diff", $"{baseRef}..HEAD"], repo), TimeSpan.FromMinutes(2), ct);
        var hits = FindSecrets(diff.Output).ToList();
        return new ScanResult(hits.Count == 0, hits.Count == 0 ? "built-in scan: clean" : "built-in scan found: " + string.Join("; ", hits));
    }

    public static IEnumerable<string> FindSecrets(string diff)
    {
        foreach (var line in diff.Split('\n'))
        {
            if (!line.StartsWith('+') || line.StartsWith("+++")) continue;
            foreach (var (name, rx) in Patterns)
                if (rx.IsMatch(line)) yield return name;
        }
    }

    private static string Tail(string s) => s.Length > 2000 ? s[^2000..] : s;

    private static readonly (string Name, Regex Rx)[] Patterns =
    [
        ("Anthropic key", AnthropicKey()), ("OpenAI-style key", OpenAiKey()), ("GitHub token", GitHubToken()),
        ("AWS access key", AwsKey()), ("Private key", PrivateKey()), ("Slack token", SlackToken()),
        ("Generic secret assignment", GenericSecret()),
    ];

    [GeneratedRegex(@"sk-ant-[A-Za-z0-9_\-]{20,}")] private static partial Regex AnthropicKey();
    [GeneratedRegex(@"\bsk-(?!ant-)[A-Za-z0-9_\-]{32,}")] private static partial Regex OpenAiKey();
    [GeneratedRegex(@"\b(ghp|gho|ghu|ghs|ghr)_[A-Za-z0-9]{30,}|github_pat_[A-Za-z0-9_]{40,}")] private static partial Regex GitHubToken();
    [GeneratedRegex(@"\bAKIA[0-9A-Z]{16}\b")] private static partial Regex AwsKey();
    [GeneratedRegex(@"-----BEGIN ([A-Z ]+ )?PRIVATE KEY-----")] private static partial Regex PrivateKey();
    [GeneratedRegex(@"\bxox[abprs]-[A-Za-z0-9-]{10,}")] private static partial Regex SlackToken();
    [GeneratedRegex(@"(?i)(api[_-]?key|secret|password|token)\s*[:=]\s*[""'][^""'\s]{16,}[""']")] private static partial Regex GenericSecret();
}
