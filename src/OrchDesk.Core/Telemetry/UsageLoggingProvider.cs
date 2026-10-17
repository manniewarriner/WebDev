using System.Runtime.CompilerServices;
using System.Text;
using OrchDesk.Core.Models;
using OrchDesk.Core.Providers;

namespace OrchDesk.Core.Telemetry;

/// <summary>Logs each transport call, including tool rounds and partial cancelled responses.</summary>
public sealed class UsageLoggingProvider(IModelProvider inner, UsageLog log, double minimumFreeRamGb = 0) : IModelProvider
{
    public string Id => inner.Id;
    public string ModelId => inner.ModelId;
    public string DisplayName => inner.DisplayName;
    public bool IsLocal => inner.IsLocal;
    public bool IsAgentic => inner.IsAgentic;
    public Task<ProviderHealth> CheckHealthAsync(CancellationToken ct) => inner.CheckHealthAsync(ct);
    public async IAsyncEnumerable<ChatEvent> StreamChatAsync(ChatRequest request, [EnumeratorCancellation] CancellationToken ct)
    {
        if (IsLocal && minimumFreeRamGb > 0) OrchDesk.Core.Safety.MemoryGuard.Ensure(minimumFreeRamGb);
        ResponseUsage? usage = null;
        var output = new StringBuilder();
        try
        {
            await foreach (var ev in inner.StreamChatAsync(request, ct))
            {
                if (ev is UsageReported reported) usage = reported.Usage;
                else if (ev is TextDelta delta) output.Append(delta.Text);
                else if (ev is ReasoningDelta reasoning) output.Append(reasoning.Text);
                yield return ev;
            }
        }
        finally
        {
            usage ??= ResponseUsage.Estimate(string.Join("\n", request.Messages.Select(m => m.Content)), output.ToString());
            log.Record(new(DateTime.UtcNow, Id + "/" + ModelId, "model", (int)Math.Clamp(usage.InputTokens, 0, int.MaxValue), (int)Math.Clamp(usage.OutputTokens, 0, int.MaxValue)));
        }
    }
}
