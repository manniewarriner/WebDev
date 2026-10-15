using OrchDesk.Core.Tools;

namespace OrchDesk.Core.Presentation;

public sealed record WorkspaceInspection(IReadOnlyDictionary<string, string> Changes, string Diff)
{
    public static async Task<WorkspaceInspection> ReadAsync(string root, CancellationToken ct = default)
    {
        if (!Directory.Exists(root)) return new(new Dictionary<string, string>(), "Choose a project folder to review its changes.");
        Task<CommandResult> Git(params string[] args) => ProcessRunner.RunAsync(ProcessRunner.Exe("git", args, root), TimeSpan.FromSeconds(10), ct);
        var changed = Git("diff", "--no-ext-diff", "--no-textconv", "--name-status", "--no-renames", "HEAD");
        var untracked = Git("ls-files", "--others", "--exclude-standard");
        var diff = Git("diff", "--no-ext-diff", "--no-textconv", "--no-color", "HEAD");
        await Task.WhenAll(changed, untracked, diff);
        var changes = ParseChanges(changed.Result.ExitCode == 0 ? changed.Result.Output : "", untracked.Result.ExitCode == 0 ? untracked.Result.Output : "");
        return new(changes, diff.Result.ExitCode == 0 ? diff.Result.Output : "No Git diff available for this folder.");
    }
    public static IReadOnlyDictionary<string, string> ParseChanges(string tracked, string untracked)
    {
        var changes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        bool Safe(string path) => !Path.IsPathRooted(path) && !path.Split('/', '\\').Any(s => s is ".." or ".github" || s.Contains("CRM", StringComparison.OrdinalIgnoreCase));
        foreach (var line in tracked.Split('\n', StringSplitOptions.RemoveEmptyEntries)) {
            var pair = line.TrimEnd('\r').Split('\t', 2);
            if (pair.Length == 2 && pair[0] is "A" or "M" or "D" && Safe(pair[1])) changes[pair[1].Replace('\\', '/')] = pair[0] == "A" ? "Added" : pair[0] == "D" ? "Deleted" : "Modified";
        }
        foreach (var line in untracked.Split('\n', StringSplitOptions.RemoveEmptyEntries)) { var path = line.TrimEnd('\r'); if (!path.StartsWith("warning:") && !path.StartsWith("fatal:") && Safe(path)) changes[path.Replace('\\', '/')] = "Added"; }
        return changes;
    }
}
