using System.Text.Json;

namespace OrchDesk.Core.Telemetry;

// What the orchestra has learned about which tier succeeds on which kind of task. Each reviewed attempt is recorded per bucket
// ("code:easy", "asset:hard"...) and tier. Before a task starts, the planner's chosen tier is nudged by that history.
public sealed class RoutingStats(string? path = null)
{
    public const int MinSamplesToTrust = 5, MinSamplesToDistrust = 4;
    public const double TrustRate = 0.8, DistrustRate = 0.25;
    private readonly object gate = new();
    private readonly Dictionary<string, Dictionary<string, int[]>> data = Load(path);

    public static string Bucket(string kind, bool hard) => (string.IsNullOrWhiteSpace(kind) ? "code" : kind.Trim().ToLowerInvariant()) + (hard ? ":hard" : ":easy");

    public void Record(string bucket, string tier, bool passed)
    {
        lock (gate)
        {
            if (!data.TryGetValue(bucket, out var tiers)) data[bucket] = tiers = [];
            if (!tiers.TryGetValue(tier, out var c)) tiers[tier] = c = [0, 0];
            c[0]++; if (passed) c[1]++;
            Save();
        }
    }

    public (int Attempts, int Passes) Get(string bucket, string tier)
    {
        lock (gate) return data.TryGetValue(bucket, out var tiers) && tiers.TryGetValue(tier, out var c) ? (c[0], c[1]) : (0, 0);
    }

    // tiers: names cheapest first. Returns the tier index to start at and a short reason when it differs from the plan.
    public (int Start, string? Reason) Suggest(string bucket, IReadOnlyList<string> tiers, int planned)
    {
        planned = Math.Clamp(planned, 0, tiers.Count);
        for (int i = 0; i < planned && i < tiers.Count; i++)
        {
            var (n, p) = Get(bucket, tiers[i]);
            if (n >= MinSamplesToTrust && p / (double)n >= TrustRate) return (i, $"{tiers[i]} passed {p} of {n} similar tasks");
        }
        int start = planned; string? reason = null;
        while (start < tiers.Count)
        {
            var (n, p) = Get(bucket, tiers[start]);
            if (n >= MinSamplesToDistrust && p / (double)n < DistrustRate) { reason = $"{tiers[start]} passed only {p} of {n} similar tasks"; start++; } else break;
        }
        return (start, reason);
    }

    private static Dictionary<string, Dictionary<string, int[]>> Load(string? path)
    {
        try { if (path != null && File.Exists(path)) return JsonSerializer.Deserialize<Dictionary<string, Dictionary<string, int[]>>>(File.ReadAllText(path)) ?? []; }
        catch (Exception e) when (e is IOException or JsonException) { /* start fresh */ }
        return [];
    }
    private void Save()
    {
        if (path == null) return;
        try { Directory.CreateDirectory(Path.GetDirectoryName(path)!); File.WriteAllText(path + ".tmp", JsonSerializer.Serialize(data)); File.Move(path + ".tmp", path, true); }
        catch (IOException) { /* learning must never break a run */ }
    }
}
