using OrchDesk.Core.Config;
using OrchDesk.Core.Models;
using OrchDesk.Core.Providers;
namespace OrchDesk.Core.Integrations;

public static class ModelProbe
{
    public static async Task RunAsync(ProviderConfig config, Func<string,IModelProvider?> create, bool force=false, CancellationToken ct=default, Action<IModelProvider,TimeSpan?>? rateLimited=null)
    {
        foreach (var model in config.AvailableModels.Where(m=>m.Length>0).Distinct()) {
            config.ModelVerifications.TryGetValue(model,out var previous);
            if (!force && !(config.IsLocal && previous?.Verified != true) && previous is not null && previous.CheckedAt > DateTimeOffset.UtcNow.AddDays(-1)) continue;
            var provider=create(model); if(provider is null) continue;
            using var timeout=CancellationTokenSource.CreateLinkedTokenSource(ct); timeout.CancelAfter(TimeSpan.FromSeconds(provider.IsLocal ? 180 : 20));
            bool verified=false, hidden=false; string reason="Probe incomplete";
            try {
                await foreach(var ev in provider.StreamChatAsync(new([ChatMessage.User("Reply OK")],[],ReadOnly:true,HostManagedTools:true,MaxOutputTokens:provider.IsLocal ? 256 : 8),timeout.Token)) {
                    var message = ev switch { UsageLimitHit u => u.Message, ProviderUnavailable u => u.Message, ErrorEvent e => e.Message, _ => "" };
                    var alternate = config.ModelBaseUrls.TryGetValue(model, out var endpoint) && !string.Equals(endpoint.TrimEnd('/'), config.BaseUrl?.TrimEnd('/'), StringComparison.OrdinalIgnoreCase);
                    var notOnPlan = new[] { "model unavailable", "model is unavailable", "not available", "not in subscription", "subscription", "free tier", "free-tier", "wrong endpoint", "not on plan", "not included in", "not part of", "model_not_found" }.Any(t => message.Contains(t, StringComparison.OrdinalIgnoreCase));
                    if(ev.HttpStatus is 400 or 403 or 404 || ((ev is ErrorEvent or ProviderUnavailable or UsageLimitHit) && (alternate || notOnPlan))) {hidden=true; reason="Model unsupported or endpoint unavailable (HTTP "+ev.HttpStatus+")"; break;}
                    if(ev is UsageLimitHit || ev.HttpStatus==429) {
                        rateLimited?.Invoke(provider,ev.RetryAfter);
                        verified=previous?.Verified==true; reason="Rate limited"; break;
                    }
                    if(ev is ProviderUnavailable unavailable && unavailable.Message.Contains("HTML",StringComparison.OrdinalIgnoreCase)) {hidden=true; reason="Wrong endpoint: HTML login page"; break;}
                    if(ev is ErrorEvent or ProviderUnavailable or UsageLimitHit) {reason=ev.HttpStatus==429?"Rate limited":"Probe unavailable"; break;}
                    if(ev is ReasoningDelta {Text.Length:>0} or TextDelta {Text.Length:>0} or Done {FinalText.Length:>0}) {verified=true; reason="Verified"; break;}
                }
            } catch(Exception) when(!ct.IsCancellationRequested) {reason="Probe timed out or unavailable";}
            if (!verified && config.ModelBaseUrls.TryGetValue(model, out var failedEndpoint) && !string.Equals(failedEndpoint.TrimEnd('/'), config.BaseUrl?.TrimEnd('/'), StringComparison.OrdinalIgnoreCase)) hidden=true;
            config.ModelVerifications[model]=new(verified,hidden,DateTimeOffset.UtcNow,reason);
        }
    }
}
