using System.Runtime.CompilerServices;
using OrchDesk.Core.Models;

namespace OrchDesk.Core.Providers;

/// <summary>Try a preferred model, retaining the configured model and its credentials as fallback.</summary>
public sealed class PreferredModelProvider(IModelProvider preferred, IModelProvider configured) : IModelProvider
{
    private IModelProvider? _active;
    public string Id => configured.Id;
    public string DisplayName => configured.DisplayName;
    public string ModelId => (_active ?? preferred).ModelId;
    public bool IsLocal => configured.IsLocal;
    public bool IsAgentic => configured.IsAgentic;
    public async Task<ProviderHealth> CheckHealthAsync(CancellationToken ct)
    {
        var health = await preferred.CheckHealthAsync(ct);
        return health == ProviderHealth.Online ? health : await configured.CheckHealthAsync(ct);
    }
    public async IAsyncEnumerable<ChatEvent> StreamChatAsync(ChatRequest request, [EnumeratorCancellation] CancellationToken ct)
    {
        _active = preferred;
        var watch = System.Diagnostics.Stopwatch.StartNew();
        ChatEvent? failure = null;
        var answered = false;
        var failed = false;
        await using (var events = preferred.StreamChatAsync(request, ct).GetAsyncEnumerator(ct))
        {
            while (true)
            {
                ChatEvent? ev = null;
                try { if (await events.MoveNextAsync()) ev = events.Current; }
                catch (Exception ex) when (!ct.IsCancellationRequested && !answered) { failed = true; failure = new ProviderUnavailable(Id, "Provider failed") { ExceptionType = ex.GetType().Name, HttpStatus = ex is HttpRequestException http ? (int?)http.StatusCode : null }; }
                if (ev is null) break;
                if (!answered && ev is ErrorEvent or ProviderUnavailable or UsageLimitHit) { failed = true; failure = ev; break; }
                if (ev is TextDelta { Text.Length: > 0 } or ToolCallRequested) answered = true;
                if (ev is Done && !answered) { failed = true; break; }
                yield return ev;
            }
        }
        if (answered && !failed) yield break;
        yield return new ModelAttemptFailed(preferred.ModelId, failure is UsageLimitHit ? "limit" : failure?.HttpStatus is 401 or 403 ? "preferred model rejected" : "preferred model unavailable", watch.ElapsedMilliseconds) { ExceptionType = failure?.ExceptionType, HttpStatus = failure?.HttpStatus };
        _active = configured;
        await foreach (var ev in configured.StreamChatAsync(request, ct)) yield return ev;
    }
}
