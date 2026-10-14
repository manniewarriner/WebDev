using System.Collections.ObjectModel;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OrchDesk.App.Services;
using OrchDesk.Core.Accounts;
using OrchDesk.Core.Providers;

namespace OrchDesk.App.ViewModels;

public sealed partial class AccountRow(AccountState state, AccountRotator rotator, int position) : ObservableObject
{
    public AccountState State => state;
    public int Position { get; } = position;
    public string Name => state.Provider.DisplayName;
    public string Kind => state.Provider switch
    {
        ClaudeCliProvider => "Claude Code CLI · subscription",
        GenericCliProvider => "Agent CLI",
        _ => state.Provider.IsLocal ? "Model on your PC" : "Cloud API",
    };

    public string HealthText => state.Health switch
    {
        ProviderHealth.Online => "Connected",
        ProviderHealth.Offline => state.Provider.IsLocal ? "PC unreachable" : "Unreachable",
        ProviderHealth.NotConfigured => "Not set up",
        _ => "Not checked yet",
    };

    public string Tone => state.Health switch
    {
        ProviderHealth.Online when rotator.IsUsable(state) => "ok",
        ProviderHealth.Online => "warn",
        ProviderHealth.Offline or ProviderHealth.NotConfigured => "bad",
        _ => "idle",
    };

    public string? LimitText => state.LastLimitHit is { } t
        ? $"Limit hit {t.ToLocalTime():ddd HH:mm}" + (rotator.IsUsable(state) ? "" : $" · resting until {(t + rotator.LimitCooldown).ToLocalTime():HH:mm}")
        : null;

    public bool HasLimit => state.LastLimitHit is not null;

    [RelayCommand]
    private void ClearLimit()
    {
        rotator.ClearLimit(state.Provider.Id);
        Refresh();
    }

    public void Refresh()
    {
        OnPropertyChanged(nameof(HealthText));
        OnPropertyChanged(nameof(Tone));
        OnPropertyChanged(nameof(LimitText));
        OnPropertyChanged(nameof(HasLimit));
    }
}

/// <summary>Shows each model in fallback order with live status. Health is re-checked every 30 s.</summary>
public sealed partial class AccountsViewModel : ObservableObject
{
    private readonly AppServices _services;
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(30) };

    public ObservableCollection<AccountRow> Rows { get; } = [];
    [ObservableProperty] private string _activeName = "—";
    [ObservableProperty] private bool _isChecking;

    public AccountsViewModel(AppServices services)
    {
        _services = services;
        _services.Rebuilt += Reload;
        _timer.Tick += async (_, _) => await CheckNowAsync();
        Reload();
        _timer.Start();
        _ = CheckNowAsync();
    }

    private void Reload()
    {
        Rows.Clear();
        var i = 1;
        foreach (var a in _services.Rotator.Accounts) Rows.Add(new AccountRow(a, _services.Rotator, i++));
        _services.Rotator.Changed += RefreshAll;
        RefreshAll();
    }

    private void RefreshAll()
    {
        System.Windows.Application.Current?.Dispatcher.Invoke(() =>
        {
            foreach (var r in Rows) r.Refresh();
            ActiveName = _services.Rotator.Current()?.DisplayName ?? "Nothing available";
        });
    }

    [RelayCommand]
    private async Task CheckNowAsync()
    {
        if (IsChecking) return;
        IsChecking = true;
        try { await _services.Rotator.RefreshHealthAsync(CancellationToken.None); }
        finally { IsChecking = false; RefreshAll(); }
    }
}
