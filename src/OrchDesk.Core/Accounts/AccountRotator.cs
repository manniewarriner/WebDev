using OrchDesk.Core.Providers;

namespace OrchDesk.Core.Accounts;

public sealed class AccountState
{
    public required IModelProvider Provider { get; init; }
    public ProviderHealth Health { get; set; } = ProviderHealth.Unknown;
    public DateTimeOffset? LastLimitHit { get; set; }
    public string? LastLimitMessage { get; set; }
    /// <summary>Last real send failure; diagnostic probes do not start the send cooldown.</summary>
    public DateTimeOffset? LastOfflineAt { get; set; }
    public DateTimeOffset? LastChecked { get; set; }
}

/// <summary>
/// Keeps the ordered fallback chain (e.g. Local PC → Claude Outlook → Claude Gmail → OpenCode) and picks the first
/// usable entry. Sends skip accounts cooling down after a real request failed or hit a usage limit.
/// Diagnostic health checks inform the UI without preventing a real send attempt.
/// </summary>
public sealed class AccountRotator
{
    private readonly List<AccountState> _chain;
    private readonly TimeProvider _time;

    public AccountRotator(IEnumerable<IModelProvider> chain, TimeSpan? limitCooldown = null, TimeProvider? time = null)
    {
        _chain = chain.Select(p => new AccountState { Provider = p }).ToList();
        LimitCooldown = limitCooldown ?? TimeSpan.FromHours(5);
        _time = time ?? TimeProvider.System;
    }

    /// <summary>How long a provider is skipped after hitting a usage limit (Claude limits reset on ~5h windows).</summary>
    public TimeSpan LimitCooldown { get; set; }
    /// <summary>How long an offline provider is skipped before being tried again without a fresh health check.</summary>
    public TimeSpan OfflineRetryAfter { get; set; } = TimeSpan.FromMinutes(2);

    public IReadOnlyList<AccountState> Accounts => _chain;
    public event Action? Changed;

    public AccountState? Find(string providerId) => _chain.FirstOrDefault(a => a.Provider.Id == providerId);

    public bool IsUsable(AccountState a, bool forSend = false)
    {
        var now = _time.GetUtcNow();
        if (!forSend && a.Health == ProviderHealth.NotConfigured) return false;
        if (a.LastLimitHit is { } hit && now - hit < LimitCooldown) return false;
        if (a.Health == ProviderHealth.Offline && a.LastOfflineAt is { } off && now - off < OfflineRetryAfter) return false;
        return true;
    }

    /// <summary>First usable provider in chain order, optionally skipping ones already tried for this message.</summary>
    public IModelProvider? Current(IReadOnlySet<string>? exclude = null, bool forSend = false) =>
        _chain.FirstOrDefault(a => IsUsable(a, forSend) && (exclude is null || !exclude.Contains(a.Provider.Id)))?.Provider;

    public async Task RefreshUnknownHealthAsync(CancellationToken ct)
    {
        await Task.WhenAll(_chain.Where(a => a.Health == ProviderHealth.Unknown).Select(async a =>
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(TimeSpan.FromSeconds(4));
            try { a.Health = await a.Provider.CheckHealthAsync(timeout.Token).WaitAsync(timeout.Token); }
            catch (Exception) when (!ct.IsCancellationRequested) { a.Health = ProviderHealth.Offline; }
            a.LastChecked = _time.GetUtcNow();
        }));
        Changed?.Invoke();
    }

    public void MarkLimitHit(string providerId, string message)
    {
        if (Find(providerId) is not { } a) return;
        a.LastLimitHit = _time.GetUtcNow();
        a.LastLimitMessage = message;
        Changed?.Invoke();
    }

    public void MarkOffline(string providerId)
    {
        if (Find(providerId) is not { } a) return;
        a.Health = ProviderHealth.Offline;
        a.LastOfflineAt = _time.GetUtcNow();
        Changed?.Invoke();
    }

    public void ClearLimit(string providerId)
    {
        if (Find(providerId) is { } a) { a.LastLimitHit = null; a.LastLimitMessage = null; Changed?.Invoke(); }
    }

    /// <summary>Health-check every provider in parallel. Call at startup and every ~30 s.</summary>
    public async Task RefreshHealthAsync(CancellationToken ct)
    {
        await Task.WhenAll(_chain.Select(async a =>
        {
            var h = await a.Provider.CheckHealthAsync(ct);
            a.Health = h;
            a.LastChecked = _time.GetUtcNow();
        }));
        Changed?.Invoke();
    }
}
