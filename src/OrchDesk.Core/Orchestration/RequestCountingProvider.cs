using System.Runtime.CompilerServices;
using OrchDesk.Core.Models;
using OrchDesk.Core.Providers;
namespace OrchDesk.Core.Orchestration;
/// <summary>Count every transport request, including AgentLoop tool rounds.</summary>
internal sealed class RequestCountingProvider(IModelProvider inner, ModelUsageTracker usage) : IModelProvider
{
    public string Id=>inner.Id;
    public string ModelId=>inner.ModelId;
    public string DisplayName=>inner.DisplayName;
    public bool IsLocal=>inner.IsLocal;
    public bool IsAgentic=>inner.IsAgentic;
    public Task<ProviderHealth> CheckHealthAsync(CancellationToken ct)=>inner.CheckHealthAsync(ct);
    public async IAsyncEnumerable<ChatEvent> StreamChatAsync(ChatRequest request,[EnumeratorCancellation] CancellationToken ct)
    {
        usage.Start(inner);
        await foreach(var ev in inner.StreamChatAsync(request,ct)) yield return ev;
    }
}
