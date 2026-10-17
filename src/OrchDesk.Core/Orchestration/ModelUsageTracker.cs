using System.Text.Json;
using OrchDesk.Core.Models;
using OrchDesk.Core.Providers;

namespace OrchDesk.Core.Orchestration;

public sealed class ModelUsage
{
    public long Requests { get; set; }
    public long Tokens { get; set; }
    public long SessionRequests { get; set; }
    public long SessionTokens { get; set; }
    public int Failures { get; set; }
    public DateTimeOffset? BackoffUntil { get; set; }
}

public sealed class ModelUsageTracker
{
    private readonly object sync = new();
    private readonly string? path;
    private readonly Func<DateTimeOffset> clock;
    private DateOnly day;
    private Dictionary<string, ModelUsage> entries = [];
    public static string Key(IModelProvider p) => p.Id + "/" + p.ModelId;
    public ModelUsageTracker(string? directory = null, Func<DateTimeOffset>? clock = null)
    {
        this.clock = clock ?? (() => DateTimeOffset.UtcNow);
        day = DateOnly.FromDateTime(this.clock().UtcDateTime);
        path = directory is null ? null : Path.Combine(directory, "model-usage.json");
        if (path is not null && File.Exists(path)) try {
            var saved = JsonSerializer.Deserialize<Saved>(File.ReadAllText(path));
            if (saved?.Day == day) entries = saved.Entries;
            foreach (var e in entries.Values) { e.SessionRequests = 0; e.SessionTokens = 0; }
        } catch (Exception ex) when (ex is IOException or JsonException) { }
    }
    private sealed record Saved(DateOnly Day, Dictionary<string, ModelUsage> Entries);
    private void Reset() { var now = DateOnly.FromDateTime(clock().UtcDateTime); if (now != day) { day = now; entries.Clear(); } }
    private ModelUsage Entry(string key) { Reset(); if (!entries.TryGetValue(key, out var e)) entries[key] = e = new(); return e; }
    private void Save() { if (path is null) return; Directory.CreateDirectory(Path.GetDirectoryName(path)!); File.WriteAllText(path + ".tmp", JsonSerializer.Serialize(new Saved(day, entries))); File.Move(path + ".tmp", path, true); }
    public IReadOnlyDictionary<string, ModelUsage> Snapshot() { lock(sync) { Reset(); return entries.ToDictionary(p => p.Key, p => new ModelUsage { Requests=p.Value.Requests, Tokens=p.Value.Tokens, SessionRequests=p.Value.SessionRequests, SessionTokens=p.Value.SessionTokens, Failures=p.Value.Failures, BackoffUntil=p.Value.BackoffUntil }); } }
    public IModelProvider? Pick(IEnumerable<IModelProvider> candidates, ISet<string>? excluded = null)
    {
        lock(sync) return candidates.Where(p => !p.ModelId.EndsWith("-free", StringComparison.OrdinalIgnoreCase) && (excluded is null || !excluded.Contains(Key(p))) && !(Entry(Key(p)).BackoffUntil > clock()))
            .OrderBy(p => Entry(Key(p)).SessionRequests).ThenBy(p => Entry(Key(p)).Requests).ThenBy(p => Entry(Key(p)).Tokens).FirstOrDefault();
    }
    public void Start(IModelProvider p) { lock(sync) { var e=Entry(Key(p)); e.Requests++; e.SessionRequests++; Save(); } }
    /// <summary>Optional long-term log (usage.jsonl, shared with the original build) that feeds the Usage page and the daily token ceiling.</summary>
    public OrchDesk.Core.Telemetry.UsageLog? Log { get; set; }
    public void Complete(IModelProvider p, ResponseUsage usage, string role = "work") { lock(sync) { var e=Entry(Key(p)); e.Tokens+=usage.InputTokens+usage.OutputTokens; e.SessionTokens+=usage.InputTokens+usage.OutputTokens; Save(); } Log?.Record(new(DateTime.UtcNow, Key(p), role, (int)Math.Min(int.MaxValue, usage.InputTokens), (int)Math.Min(int.MaxValue, usage.OutputTokens))); }
    public void Backoff(IModelProvider p, TimeSpan? retryAfter = null) { lock(sync) { var e=Entry(Key(p)); var minutes = new[] {2,5,15}[Math.Min(e.Failures++,2)]; var delay=TimeSpan.FromMinutes(minutes); if(retryAfter > delay) delay=retryAfter.Value; e.BackoffUntil=clock()+delay; Save(); } }
}
