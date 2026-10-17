using System.Runtime.CompilerServices;
using OrchDesk.Core.Models;
using OrchDesk.Core.Providers;
namespace OrchDesk.Core.Orchestration;
/// <summary>Count every transport request, including AgentLoop tool rounds.</summary>
internal sealed class RequestCountingProvider(IModelProvider inner, ModelUsageTracker usage, int maxZeroOutputRequests = 0, Action? beforeRequest = null) : IModelProvider
{
    private int zeroOutputRequests;
    public string Id=>inner.Id;
    public string ModelId=>inner.ModelId;
    public string DisplayName=>inner.DisplayName;
    public bool IsLocal=>inner.IsLocal;
    public bool IsAgentic=>inner.IsAgentic;
    public Task<ProviderHealth> CheckHealthAsync(CancellationToken ct)=>inner.CheckHealthAsync(ct);
    public async IAsyncEnumerable<ChatEvent> StreamChatAsync(ChatRequest request,[EnumeratorCancellation] CancellationToken ct)
    {
        beforeRequest?.Invoke();
        usage.Start(inner);
        bool output=false;
        await foreach(var ev in inner.StreamChatAsync(request,ct)) {
            if(ev is TextDelta {Text.Length:>0} or ReasoningDelta {Text.Length:>0} or UsageReported {Usage.OutputTokens:>0} or Done {FinalText.Length:>0}) output=true;
            yield return ev;
        }
        zeroOutputRequests=output ? 0 : zeroOutputRequests+1;
        if(maxZeroOutputRequests>0 && zeroOutputRequests>=maxZeroOutputRequests)
            throw new TimeoutException($"Worker stalled: {zeroOutputRequests} consecutive requests without output tokens.");
    }
}
