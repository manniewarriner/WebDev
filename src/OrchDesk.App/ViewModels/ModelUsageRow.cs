using OrchDesk.Core.Orchestration;
namespace OrchDesk.App.ViewModels;

public sealed record ModelUsageRow(string Model, long TodayRequests, long TodayTokens, long SessionRequests, long SessionTokens, string Backoff)
{
    public static IReadOnlyList<ModelUsageRow> Read(ModelUsageTracker tracker) => tracker.Snapshot()
        .OrderBy(p => p.Key, StringComparer.OrdinalIgnoreCase)
        .Select(p => new ModelUsageRow(p.Key, p.Value.Requests, p.Value.Tokens, p.Value.SessionRequests, p.Value.SessionTokens,
            p.Value.BackoffUntil > DateTimeOffset.UtcNow ? $"Until {p.Value.BackoffUntil.Value.ToLocalTime():HH:mm}" : "Ready")).ToArray();
}
