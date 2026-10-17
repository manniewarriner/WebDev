using System.Runtime.CompilerServices;
using OrchDesk.Core.Config;
using OrchDesk.Core.Models;
using OrchDesk.Core.Providers;
using OrchDesk.Core.Telemetry;

namespace OrchDesk.Core.Automation;

/// <summary>Shared by every provider in one unattended run; checks caps before every tool round or failover.</summary>
public sealed class RunBudget(AppConfig config, UsageLog usage)
{
    private int calls;
    public IModelProvider Wrap(IModelProvider provider) => new BudgetProvider(provider, this);
    private void Start()
    {
        if (config.DailyTokenCeiling > 0 && usage.Summarize(1).Tokens >= config.DailyTokenCeiling)
            throw new InvalidOperationException("Daily token ceiling reached.");
        if (Interlocked.Increment(ref calls) > Math.Max(1, config.MaxModelCalls))
            throw new InvalidOperationException("Run stopped: model call budget spent.");
    }
    private sealed class BudgetProvider(IModelProvider inner, RunBudget budget) : IModelProvider
    {
        public string Id => inner.Id; public string ModelId => inner.ModelId; public string DisplayName => inner.DisplayName;
        public bool IsLocal => inner.IsLocal; public bool IsAgentic => inner.IsAgentic;
        public Task<ProviderHealth> CheckHealthAsync(CancellationToken ct) => inner.CheckHealthAsync(ct);
        public async IAsyncEnumerable<ChatEvent> StreamChatAsync(ChatRequest request, [EnumeratorCancellation] CancellationToken ct)
        {
            budget.Start();
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(TimeSpan.FromMinutes(Math.Clamp(budget.config.CallTimeoutMinutes, 1, 120)));
            await foreach (var ev in inner.StreamChatAsync(request, timeout.Token)) yield return ev;
        }
    }
    private AppConfig config { get; } = config;
}
