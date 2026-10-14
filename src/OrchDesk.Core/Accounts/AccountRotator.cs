using OrchDesk.Core.Providers;

namespace OrchDesk.Core.Accounts;

public sealed class AccountState
{
    public required IModelProvider Provider { get; init; }
    public ProviderHealth Health { get; set; } = ProviderHealth.Unknown;
    public DateTimeOffset? LastLimitHit { get; set; }
    public string? LastLimitMessage { get; set; }
    public DateTimeOffset? LastOfflineAt { get; set; }
    public DateTimeOffset? LastChecked { get; set; }
}

/// <summary>
/// Keeps the ordered fallback chain (e.g. Local PC → Claude Outlook → Claude Gmail → OpenCode) and picks the first
/// usable entry. An account is skipped while it's cooling down after a usage limit, or while its health
/// check says it's offline (e.g. the desktop PC is switched off). Re-checks happen on a timer from the UI/runner.
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

    public bool IsUsable(AccountState a)
    {
        var now = _time.GetUtcNow();
        if (a.Health == ProviderHealth.NotConfigured) return false;
        if (a.LastLimitHit is { } hit && now - hit < LimitCooldown) return false;
        if (a.Health == ProviderHealth.Offline && a.LastOfflineAt is { } off && now - off < OfflineRetryAfter) return false;
        return true;
    }

    /// <summary>First usable provider in chain order, optionally skipping ones already tried for this message.</summary>
    public IModelProvider? Current(IReadOnlySet<string>? exclude = null) =>
        _chain.FirstOrDefault(a => IsUsable(a) && (exclude is null || !exclude.Contains(a.Provider.Id)))?.Provider;

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
            if (h == ProviderHealth.Offline) a.LastOfflineAt = a.LastChecked;
        }));
        Changed?.Invoke();
    }
}
