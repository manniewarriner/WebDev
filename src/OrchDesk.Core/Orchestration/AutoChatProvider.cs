using System.Runtime.CompilerServices;
using OrchDesk.Core.Models;
using OrchDesk.Core.Providers;
namespace OrchDesk.Core.Orchestration;
public sealed class AutoChatProvider(IReadOnlyList<IModelProvider> models, ModelUsageTracker usage) : IModelProvider
{
    private IModelProvider? active;
    public string Id => "auto-pool";
    public string ModelId => active?.ModelId ?? "Auto";
    public string DisplayName => active?.DisplayName ?? "Auto model";
    public bool IsLocal => false;
    public bool IsAgentic => false;
    public Task<ProviderHealth> CheckHealthAsync(CancellationToken ct)=>Task.FromResult(models.Count>0?ProviderHealth.Online:ProviderHealth.NotConfigured);
    public async IAsyncEnumerable<ChatEvent> StreamChatAsync(ChatRequest request,[EnumeratorCancellation] CancellationToken ct)
    {
        var tried=new HashSet<string>();
        while((active=usage.Pick(models,tried)) is { } model) {
            tried.Add(ModelUsageTracker.Key(model)); usage.Start(model);
            ResponseUsage? measured=null; var output=new System.Text.StringBuilder(); bool failed=false; bool answered=false;
            using var timeout=CancellationTokenSource.CreateLinkedTokenSource(ct);
            await using var stream=model.StreamChatAsync(request with {HostManagedTools=true},timeout.Token).GetAsyncEnumerator(timeout.Token);
            var first=true;
            while(true) {
                bool more=false;
                try { timeout.CancelAfter(first?TimeSpan.FromSeconds(20):TimeSpan.FromMinutes(5)); more=await stream.MoveNextAsync(); }
                catch(Exception) when(!ct.IsCancellationRequested) { failed=true; }
                if(failed || !more) break;
                first=false; var ev=stream.Current;
                if(ev is UsageReported u) measured=u.Usage;
                if(ev is TextDelta d) output.Append(d.Text);
                if(ev is TextDelta {Text.Length:>0} or ToolCallRequested or Done {FinalText.Length:>0}) answered=true;
                if(ev is UsageLimitHit || ev.HttpStatus==429) { usage.Backoff(model,ev.RetryAfter); failed=true; break; }
                if(ev is ErrorEvent or ProviderUnavailable) { failed=true; break; }
                yield return ev;
            }
            usage.Complete(model,measured??ResponseUsage.Estimate(string.Join("\n",request.Messages.Select(m=>m.Content)),output.ToString()));
            if(!failed && answered) yield break;
            yield return new StatusUpdate($"Plain failover: {model.Id}/{model.ModelId} failed or stalled; trying another verified model.");
        }
        yield return new ProviderUnavailable(Id,"All Auto models unavailable or cooling down.");
    }
}

