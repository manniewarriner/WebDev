using System.Text;
using OrchDesk.Core.Accounts;

namespace OrchDesk.Core.Overnight;

/// <summary>Writes reports/overnight-YYYY-MM-DD.md: a summary table, then per-task details and how to review.</summary>
public static class MorningReport
{
    public static string Write(string reportsDir, DateTimeOffset started, IReadOnlyList<OvernightTask> tasks, AccountRotator rotator)
    {
        Directory.CreateDirectory(reportsDir);
        var path = Path.Combine(reportsDir, $"overnight-{started:yyyy-MM-dd}.md");
        File.WriteAllText(path, Build(started, DateTimeOffset.Now, tasks, rotator));
        return path;
    }

    public static string Build(DateTimeOffset started, DateTimeOffset finished, IReadOnlyList<OvernightTask> tasks, AccountRotator rotator)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"# OrchDesk overnight report — {started:dddd d MMMM yyyy}");
        sb.AppendLine();
        sb.AppendLine($"Ran {started:HH:mm}–{finished:HH:mm} · {tasks.Count} task(s) · " +
                      $"{tasks.Count(t => t.Status == OvernightStatus.Succeeded)} succeeded · " +
                      $"{tasks.Count(t => t.Status is OvernightStatus.Failed or OvernightStatus.NeedsAttention)} need you");
        sb.AppendLine();

        if (tasks.Count == 0) { sb.AppendLine("The queue was empty."); return sb.ToString(); }

        sb.AppendLine("| Task | Result | Tests | Model | Branch |");
        sb.AppendLine("|---|---|---|---|---|");
        foreach (var t in tasks)
            sb.AppendLine($"| {Esc(t.Title)} | {Icon(t.Status)} {t.Status} | {(t.TestsPassed switch { true => "pass", false => "fail", _ => "—" })} | {Esc(t.ProviderUsed ?? "—")} | `{t.Branch}` |");
        sb.AppendLine();

        var limited = rotator.Accounts.Where(a => a.LastLimitHit is { } h && h >= started).ToList();
        if (limited.Count > 0)
        {
            sb.AppendLine("## Usage limits hit");
            foreach (var a in limited) sb.AppendLine($"- {a.Provider.DisplayName} at {a.LastLimitHit:HH:mm}: {a.LastLimitMessage}");
            sb.AppendLine();
        }

        foreach (var t in tasks)
        {
            sb.AppendLine($"## {Icon(t.Status)} {Esc(t.Title)}");
            sb.AppendLine();
            sb.AppendLine($"- Folder: `{t.Workspace}`");
            sb.AppendLine($"- Branch: `{t.Branch}` (worktree `{t.WorktreePath}`){(t.Pushed ? " — pushed" : "")}");
            sb.AppendLine($"- Attempts: {t.Attempts} · Duration: {(t.FinishedAt - t.StartedAt)?.ToString(@"h\:mm\:ss") ?? "—"}");
            if (t.Error is not null) sb.AppendLine($"- **Problem:** {t.Error}");
            foreach (var n in t.Notes) sb.AppendLine($"- {n}");
            if (!string.IsNullOrWhiteSpace(t.DiffStat)) sb.AppendLine().AppendLine("```").AppendLine(t.DiffStat).AppendLine("```");
            if (!string.IsNullOrWhiteSpace(t.Summary)) sb.AppendLine().AppendLine("> " + t.Summary.Replace("\n", "\n> "));
            sb.AppendLine();
            sb.AppendLine($"Review: `git -C \"{t.Workspace}\" log -p HEAD..{t.Branch}` · Merge: `git merge {t.Branch}` · Full log: `{t.LogPath}`");
            sb.AppendLine();
        }
        return sb.ToString();
    }

    private static string Icon(OvernightStatus s) => s switch
    {
        OvernightStatus.Succeeded => "✅",
        OvernightStatus.Failed => "❌",
        OvernightStatus.NeedsAttention => "⚠️",
        OvernightStatus.Cancelled => "⏹",
        _ => "•",
    };

    private static string Esc(string s) => s.Replace("|", "\\|").Replace("\n", " ");
}
