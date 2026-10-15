using System.Text.Json;
using System.Text.RegularExpressions;
namespace OrchDesk.Core.Activity;

public sealed record ActivityDay(DateOnly Date, int Messages, int Runs) { public int Total => Messages + Runs; public int Level => Total == 0 ? 0 : Total < 3 ? 1 : Total < 7 ? 2 : Total < 15 ? 3 : 4; }
public static class ActivityAggregator
{
    public static IReadOnlyList<ActivityDay> Read(string root, DateOnly today, int weeks = 26)
    {
        if (weeks < 1 || weeks > 52) throw new ArgumentOutOfRangeException(nameof(weeks));
        var start = today.AddDays(-(weeks * 7 - 1));
        var counts = new Dictionary<DateOnly, (int messages, int runs)>();
        void Add(DateOnly date, int messages, int runs) { if (date < start || date > today) return; counts.TryGetValue(date, out var old); counts[date] = (old.messages + messages, old.runs + runs); }
        foreach (var folder in new[] { "conversations", "history", "runs", "reports" })
        {
            var directory = Path.Combine(root, folder); if (!Directory.Exists(directory)) continue;
            string[] files;
            try { files = Directory.GetFiles(directory, "*", SearchOption.AllDirectories); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { continue; }
            foreach (var file in files)
            {
                if (file.Split(Path.DirectorySeparatorChar).Any(s => s.Contains("CRM", StringComparison.OrdinalIgnoreCase))) continue;
                try
                {
                    if (Path.GetExtension(file).Equals(".json", StringComparison.OrdinalIgnoreCase))
                    {
                        using var doc = JsonDocument.Parse(File.ReadAllText(file)); var data = doc.RootElement;
                        JsonElement Get(JsonElement e, string name) => e.ValueKind == JsonValueKind.Object ? e.EnumerateObject().FirstOrDefault(p => p.Name.Equals(name, StringComparison.OrdinalIgnoreCase)).Value : default;
                        DateOnly Date(JsonElement e) { var stamp = Get(e, "timestamp"); if (stamp.ValueKind != JsonValueKind.String) stamp = Get(e, "startedAt"); return stamp.ValueKind == JsonValueKind.String && DateTimeOffset.TryParse(stamp.GetString(), out var at) ? DateOnly.FromDateTime(at.LocalDateTime) : DateOnly.FromDateTime(File.GetLastWriteTime(file)); }
                        if (folder == "runs")
                        {
                            if (Path.GetFileName(file) == "checkpoint.json" || Path.GetDirectoryName(file) == directory)
                            {
                                var date = Date(data);
                                var log = Path.Combine(Path.GetDirectoryName(file)!, "progress.log");
                                if (File.Exists(log)) { var first = File.ReadLines(log).FirstOrDefault()?.Split(' ')[0]; if (DateTimeOffset.TryParse(first, out var started)) date = DateOnly.FromDateTime(started.LocalDateTime); }
                                Add(date, 0, 1);
                            }
                        }
                        else if (folder != "reports") { var messages = Get(data, "messages"); if (messages.ValueKind == JsonValueKind.Array) foreach (var message in messages.EnumerateArray()) Add(Date(message), 1, 0); }
                    }
                    else if (folder == "reports" && Path.GetFileName(file).StartsWith("overnight-") && Path.GetExtension(file) == ".md")
                    {
                        var match = Regex.Match(Path.GetFileName(file), @"\d{4}-\d{2}-\d{2}");
                        if (DateOnly.TryParse(match.Value, out var date)) { var text = File.ReadAllText(file); var tasks = Regex.Match(text, @"(\d+) task\(s\)"); if (tasks.Success) Add(date, 0, int.Parse(tasks.Groups[1].Value)); }
                    }
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException) { }
            }
        }
        return Enumerable.Range(0, weeks * 7).Select(i => { var date = start.AddDays(i); counts.TryGetValue(date, out var n); return new ActivityDay(date, n.messages, n.runs); }).ToArray();
    }
}
