using System.Text.Json;
using OrchDesk.Core.Safety;

namespace OrchDesk.Core.Telemetry;

public sealed record RunLogEntry(string At, string Kind, string TaskId, string Title, string Tier, string Role, string Text, string Project = "");
public sealed record RunLogSummary(string Path, DateTime Started, int Events, string FirstText);

/// <summary>
/// One JSON line per run event in runs\&lt;timestamp&gt;-&lt;id&gt;.jsonl (same files the original build wrote), so a finished run can be
/// audited later. Only the newest <c>keep</c> logs are kept; overnight checkpoint folders under runs\ are never touched.
/// </summary>
public sealed class RunLog
{
    private readonly object gate = new();
    private readonly Func<string, string> redact;
    public string Path { get; }

    public RunLog(string directory, Func<string, string>? redact = null, int keep = 30)
    {
        this.redact = redact ?? Redaction.Clean;
        Directory.CreateDirectory(directory);
        Path = System.IO.Path.Combine(directory, DateTime.Now.ToString("yyyyMMdd-HHmmss") + "-" + Guid.NewGuid().ToString("N")[..4] + ".jsonl");
        foreach (var old in new DirectoryInfo(directory).GetFiles("*.jsonl").OrderByDescending(f => f.Name).Skip(Math.Max(1, keep) - 1))
            try { old.Delete(); } catch (IOException) { /* in use; leave it */ }
    }

    public void Write(string kind, string text, string taskId = "", string title = "", string tier = "", string role = "", string project = "")
    {
        var safeText = redact(text);
        var line = JsonSerializer.Serialize(new { at = DateTimeOffset.Now.ToString("O"), kind, TaskId = taskId, Title = redact(title), Tier = redact(tier), Role = redact(role), Project = redact(project), text = safeText.Length > 4000 ? safeText[..4000] : safeText });
        lock (gate) { try { File.AppendAllText(Path, line + "\n"); } catch (IOException) { /* logging must never break a run */ } }
    }

    /// <summary>Reads a log written by either build. Damaged lines are skipped.</summary>
    public static IReadOnlyList<RunLogEntry> Read(string path)
    {
        var list = new List<RunLogEntry>();
        try
        {
            foreach (var line in File.ReadLines(path))
            {
                if (string.IsNullOrWhiteSpace(line)) continue;
                try
                {
                    using var doc = JsonDocument.Parse(line); var r = doc.RootElement;
                    string S(string n) { foreach (var p in r.EnumerateObject()) if (p.Name.Equals(n, StringComparison.OrdinalIgnoreCase)) return p.Value.ValueKind == JsonValueKind.String ? p.Value.GetString()! : p.Value.ToString(); return ""; }
                    list.Add(new(S("at"), S("kind"), S("TaskId"), S("Title"), S("Tier"), S("Role"), S("text"), S("Project")));
                }
                catch (JsonException) { }
            }
        }
        catch (IOException) { }
        return list;
    }

    public static IReadOnlyList<RunLogSummary> Recent(string directory, int max = 20)
    {
        if (!Directory.Exists(directory)) return [];
        return new DirectoryInfo(directory).GetFiles("*.jsonl").OrderByDescending(f => f.Name).Take(max).Select(f =>
        {
            var entries = Read(f.FullName);
            return new RunLogSummary(f.FullName, f.CreationTime, entries.Count, entries.FirstOrDefault(e => e.Text.Length > 0)?.Text ?? "");
        }).ToList();
    }
}
