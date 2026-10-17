using System.Text.Json;

namespace OrchDesk.Core.Telemetry;

public sealed record UsageEntry(DateTime Utc, string Model, string Role, int InputTokens, int OutputTokens)
{
    public int Total => InputTokens + OutputTokens;
}
public sealed record DayUsage(DateOnly Date, long Tokens, int Calls);
public sealed record UsageSummary(long Tokens, int Calls, DayUsage? BusiestDay, int Streak, IReadOnlyList<(string Model, long Tokens)> ByModel);

// Every model call is logged as one JSON line (usage.jsonl). Providers rarely report exact token counts, so tokens are estimated from text length
// (about four characters per token). The log feeds the Usage skyline.
public sealed class UsageLog(string? path = null)
{
    private readonly object gate = new();
    private readonly List<UsageEntry> entries = Load(path);

    public static int Estimate(string? text) => string.IsNullOrEmpty(text) ? 0 : (text.Length + 3) / 4;
    public int Count { get { lock (gate) return entries.Count; } }

    public void Record(UsageEntry entry)
    {
        lock (gate)
        {
            entries.Add(entry);
            if (path == null) return;
            try { Directory.CreateDirectory(Path.GetDirectoryName(path)!); File.AppendAllText(path, JsonSerializer.Serialize(entry) + "\n"); }
            catch (IOException) { /* tracking must never break a run */ }
        }
    }

    // One bucket per local calendar day, oldest first, ending at `today` (days with no calls are included with zero).
    public IReadOnlyList<DayUsage> Daily(int days, DateOnly? today = null)
    {
        var end = today ?? DateOnly.FromDateTime(DateTime.Now); var start = end.AddDays(-(days - 1));
        Dictionary<DateOnly, (long Tokens, int Calls)> byDay = [];
        lock (gate)
            foreach (var e in entries)
            {
                var day = DateOnly.FromDateTime(e.Utc.ToLocalTime()); if (day < start || day > end) continue;
                var (t, c) = byDay.GetValueOrDefault(day); byDay[day] = (t + e.Total, c + 1);
            }
        return Enumerable.Range(0, days).Select(i => { var d = start.AddDays(i); var (t, c) = byDay.GetValueOrDefault(d); return new DayUsage(d, t, c); }).ToList();
    }

    public UsageSummary Summarize(int days, DateOnly? today = null)
    {
        var daily = Daily(days, today); var end = today ?? DateOnly.FromDateTime(DateTime.Now); var start = end.AddDays(-(days - 1));
        var busiest = daily.Where(d => d.Tokens > 0).OrderByDescending(d => d.Tokens).FirstOrDefault();
        // Streak: consecutive active days ending today, or yesterday when today has no activity yet.
        int i = daily.Count - 1; if (i >= 0 && daily[i].Tokens == 0) i--;
        int streak = 0; while (i >= 0 && daily[i].Tokens > 0) { streak++; i--; }
        List<(string Model, long Tokens)> models;
        lock (gate) models = entries.Where(e => { var d = DateOnly.FromDateTime(e.Utc.ToLocalTime()); return d >= start && d <= end; }).GroupBy(e => e.Model).Select(g => (g.Key, g.Sum(e => (long)e.Total))).OrderByDescending(x => x.Item2).ToList();
        return new(daily.Sum(d => d.Tokens), daily.Sum(d => d.Calls), busiest, streak, models);
    }

    private static List<UsageEntry> Load(string? path)
    {
        var list = new List<UsageEntry>();
        try
        {
            if (path != null && File.Exists(path))
                foreach (var line in File.ReadLines(path))
                {
                    if (string.IsNullOrWhiteSpace(line)) continue;
                    try { if (JsonSerializer.Deserialize<UsageEntry>(line) is { } e) list.Add(e); } catch (JsonException) { /* skip a damaged line */ }
                }
        }
        catch (IOException) { /* start empty */ }
        return list;
    }
}
