using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OrchDesk.App.Services;
using OrchDesk.Core.Config;

namespace OrchDesk.App.ViewModels;

/// <summary>
/// Editable copy of one provider. API keys are protected immediately; saved secrets are never shown back.
/// Save persists the encrypted values to config.json.
/// </summary>
public sealed partial class ProviderEdit : ObservableObject
{
    public ProviderEdit(ProviderConfig c)
    {
        _id = c.Id; _displayName = c.DisplayName; _kind = c.Kind; _enabled = c.Enabled; _isLocal = c.IsLocal;
        _baseUrl = c.BaseUrl; _model = c.Model; _executable = c.Executable; _claudeConfigDir = c.ClaudeConfigDir;
        _permissionMode = c.PermissionMode;
        _apiKeyProtected = c.ApiKeyProtected;
        Original = c; ModelBaseUrls = new(c.ModelBaseUrls); AvailableModels = [.. c.AvailableModels]; ModelsSyncedAt = c.ModelsSyncedAt;
    }

    public ProviderConfig Original { get; }
    public List<string> AvailableModels { get; set; } = [];
    public Dictionary<string, string> ModelBaseUrls { get; set; } = [];
    public DateTimeOffset? ModelsSyncedAt { get; set; }
    public string Badge => IsClaude ? "C" : "O";
    public string Subtitle => IsClaude ? "Claude Code · Subscription account" : "OpenCode · Go & Zen models";
    public string ConnectionState => !Enabled ? "Not connected" : ModelsSyncedAt is not null ? "Connected" : "Needs login";
    public string SyncSummary => ModelsSyncedAt is { } synced
        ? $"Synced {Math.Max(0, (int)(DateTimeOffset.Now - synced).TotalMinutes)}m ago · {AvailableModels.Count} models"
        : "Not synced yet · Connect to discover models";
    public void RefreshConnection()
    {
        OnPropertyChanged(nameof(ConnectionState));
        OnPropertyChanged(nameof(SyncSummary));
    }
    partial void OnEnabledChanged(bool value) => RefreshConnection();
    [ObservableProperty] private string _status = "Not tested";
    [ObservableProperty] private bool _testing;
    [ObservableProperty] private string _id;
    [ObservableProperty] private string _displayName;
    [ObservableProperty, NotifyPropertyChangedFor(nameof(IsHttp), nameof(IsClaude), nameof(IsCli))] private ProviderKind _kind;
    [ObservableProperty] private bool _enabled;
    [ObservableProperty] private bool _isLocal;
    [ObservableProperty] private string? _baseUrl;
    [ObservableProperty] private string? _model;
    [ObservableProperty] private string? _executable;
    [ObservableProperty] private string? _claudeConfigDir;
    [ObservableProperty] private string? _permissionMode;

    private string? _apiKeyProtected;
    public void StoreApiKey(string key, ISecretProtector secrets)
    {
        if (string.IsNullOrWhiteSpace(key)) return;
        _apiKeyProtected = secrets.Protect(key.Trim());
        OnPropertyChanged(nameof(KeyState));
    }
    public string? NewAccessClientId { get; set; }
    public string? NewAccessClientSecret { get; set; }
    public bool ClearSecrets { get; set; }

    public bool IsHttp => Kind == ProviderKind.OpenAiCompatible;
    public bool IsClaude => Kind == ProviderKind.ClaudeCli;
    public bool IsCli => Kind != ProviderKind.OpenAiCompatible;
    public string KeyState => string.IsNullOrEmpty(_apiKeyProtected) ? "No key saved" : "Key saved";

    public ProviderConfig ToConfig(ISecretProtector secrets)
    {
        string? Keep(string? newValue, string? old) =>
            ClearSecrets ? null : string.IsNullOrWhiteSpace(newValue) ? old : secrets.Protect(newValue.Trim());
        return new ProviderConfig
        {
            ModelBaseUrls = new(ModelBaseUrls), AvailableModels = [.. AvailableModels], ModelsSyncedAt = ModelsSyncedAt,
            Id = Id.Trim(), DisplayName = DisplayName.Trim(), Kind = Kind, Enabled = Enabled, IsLocal = IsLocal,
            BaseUrl = BaseUrl?.Trim(), Model = Model?.Trim(), Executable = Executable?.Trim(),
            ClaudeConfigDir = ClaudeConfigDir?.Trim(), PermissionMode = PermissionMode?.Trim(),
            ApiKeyProtected = ClearSecrets ? null : _apiKeyProtected,
            AccessClientIdProtected = Keep(NewAccessClientId, Original.AccessClientIdProtected),
            AccessClientSecretProtected = Keep(NewAccessClientSecret, Original.AccessClientSecretProtected),
        };
    }
}

public sealed partial class SettingsViewModel : ObservableObject
{
    private readonly AppServices _services;

    public ObservableCollection<ProviderEdit> Providers { get; }
    public IEnumerable<ProviderEdit> ClaudeProfiles => Providers.Where(p => p.Id is "claude-gmail" or "claude-outlook");
    public IEnumerable<ProviderEdit> OpenCodeProfiles => Providers.Where(p => p.Id == "opencode-go");
    public IEnumerable<ProviderEdit> OtherProviders => Providers.Except(ClaudeProfiles).Except(OpenCodeProfiles);
    public IReadOnlyList<ProviderKind> Kinds { get; } = Enum.GetValues<ProviderKind>();
    public IReadOnlyList<string> PermissionModes { get; } = ["acceptEdits", "default", "plan"];

    [ObservableProperty] private ProviderEdit? _selected;
    [ObservableProperty] private string _rotationOrder;
    [ObservableProperty] private int _maxSteps;
    [ObservableProperty] private int _limitCooldownMinutes;
    [ObservableProperty] private string _extraAllowedCommands;
    [ObservableProperty] private string _plannerId;
    [ObservableProperty] private string _workerId;
    [ObservableProperty] private string _testCommand;
    [ObservableProperty] private int _maxRounds;
    [ObservableProperty] private string _costTiers;
    [ObservableProperty] private int _maxMinutesPerTask;
    [ObservableProperty] private int _maxAttemptsPerTask;
    [ObservableProperty] private bool _keepPcAwake;
    [ObservableProperty] private bool _pushBranches;
    [ObservableProperty] private string? _error;
    [ObservableProperty] private string? _savedMessage;
    [ObservableProperty] private bool _webFetchEnabled;
    [ObservableProperty] private bool _shutdownWhenDone;
    [ObservableProperty] private string _webStatus = "Not tested";
    [ObservableProperty] private string _meshyStatus = "Not tested";
    public string? NewMeshyKey { get; set; }

    public event Action? Saved;

    public SettingsViewModel(AppServices services)
    {
        _services = services;
        _webFetchEnabled = services.Config.WebFetchEnabled;
        _shutdownWhenDone = services.Config.Overnight.ShutdownWhenDone;
        var c = services.Config;
        Providers = new(c.Providers.Select(p => new ProviderEdit(p)));
        foreach (var builtin in AppConfig.CreateDefault().Providers.Where(p => p.Id is "claude-gmail" or "claude-outlook" or "opencode-go"))
            if (!Providers.Any(p => p.Id == builtin.Id)) { builtin.Enabled = false; Providers.Add(new(builtin)); }
        Selected = Providers.FirstOrDefault();
        _rotationOrder = string.Join(", ", c.RotationOrder);
        _maxSteps = c.MaxSteps;
        _limitCooldownMinutes = c.LimitCooldownMinutes;
        _extraAllowedCommands = string.Join(Environment.NewLine, c.ExtraAllowedCommands);
        _plannerId = c.Supervisor.PlannerProviderId;
        _workerId = c.Supervisor.WorkerProviderId;
        _testCommand = c.Supervisor.TestCommand;
        _maxRounds = c.Supervisor.MaxRounds;
        _costTiers = string.Join(", ", c.Supervisor.CostTiers);
        _maxMinutesPerTask = c.Overnight.MaxMinutesPerTask;
        _maxAttemptsPerTask = c.Overnight.MaxAttemptsPerTask;
        _keepPcAwake = c.Overnight.KeepPcAwake;
        _pushBranches = c.Overnight.PushBranches;
    }

    [RelayCommand]
    private Task ConnectIntegrationAsync(ProviderEdit? provider) => CheckIntegrationAsync(provider, true, true);
    [RelayCommand]
    private Task TestIntegrationAsync(ProviderEdit? provider) => CheckIntegrationAsync(provider, false, false);
    [RelayCommand]
    private Task SyncIntegrationAsync(ProviderEdit? provider) => CheckIntegrationAsync(provider, true, false);

    private async Task CheckIntegrationAsync(ProviderEdit? provider, bool sync, bool connect)
    {
        if (provider is null || provider.Testing) return;
        provider.Testing = true;
        provider.Status = "Checking?";
        try
        {
            if (connect) provider.Enabled = true;
            using var http = new System.Net.Http.HttpClient();
            var config = provider.ToConfig(_services.Secrets);
            var service = new OrchDesk.Core.Integrations.IntegrationModelSync(http, _services.Secrets, new OrchDesk.Core.Integrations.ClaudeAuthRunner());
            var result = await service.RunAsync(config, sync);
            provider.Status = result.Message;
            if (!result.Ok) provider.ModelsSyncedAt = null;
            if (result.Ok && sync)
            {
                provider.ModelBaseUrls = config.ModelBaseUrls;
                provider.AvailableModels = config.AvailableModels;
                provider.ModelsSyncedAt = config.ModelsSyncedAt;
                await SaveAsync();
            }
        }
        finally { provider.Testing = false; provider.RefreshConnection(); }
    }

    public void StoreApiKey(ProviderEdit provider, string key) => provider.StoreApiKey(key, _services.Secrets);

    private OrchDesk.Core.Integrations.ConnectionTester Tester() => new(new System.Net.Http.HttpClient { Timeout = TimeSpan.FromSeconds(60) }, _services.Secrets);
    [RelayCommand]
    private async Task TestConnectionAsync(ProviderEdit? provider)
    {
        if (provider is null || provider.Testing) return;
        provider.Testing = true; provider.Status = "Testing?";
        try { var result = await Tester().TestAsync(provider.ToConfig(_services.Secrets)); provider.Status = $"{result.Message} ({result.ElapsedMs} ms)"; }
        finally { provider.Testing = false; provider.RefreshConnection(); }
    }
    [RelayCommand]
    private async Task TestWebAsync() => WebStatus = (await Tester().TestWebAsync(WebFetchEnabled)).Message;
    [RelayCommand]
    private async Task TestMeshyAsync() => MeshyStatus = (await Tester().TestMeshyAsync(string.IsNullOrWhiteSpace(NewMeshyKey) ? _services.Config.MeshyApiKeyProtected : _services.Secrets.Protect(NewMeshyKey))).Message;

    [RelayCommand]
    private void AddProvider()
    {
        var p = new ProviderEdit(new ProviderConfig
        {
            Id = $"provider-{Providers.Count + 1}", DisplayName = "New model", Kind = ProviderKind.OpenAiCompatible,
            BaseUrl = "http://127.0.0.1:1234/v1",
        });
        Providers.Add(p);
        OnPropertyChanged(nameof(OtherProviders));
        Selected = p;
    }

    [RelayCommand]
    private void RemoveProvider()
    {
        if (Selected is null) return;
        var i = Providers.IndexOf(Selected);
        Providers.Remove(Selected);
        OnPropertyChanged(nameof(OtherProviders));
        Selected = Providers.ElementAtOrDefault(Math.Max(0, i - 1));
    }

    private static List<string> SplitList(string s) =>
        s.Split([',', '\n', '\r'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();

    [RelayCommand]
    private async Task SaveAsync()
    {
        var ids = Providers.Select(p => p.Id.Trim()).ToList();
        if (ids.Any(string.IsNullOrEmpty) || ids.Distinct(StringComparer.OrdinalIgnoreCase).Count() != ids.Count)
        {
            Error = "Every model needs a unique ID.";
            return;
        }
        var order = SplitList(RotationOrder);
        if (order.FirstOrDefault(id => !ids.Contains(id)) is { } unknown)
        {
            Error = $"Fallback order mentions '{unknown}', which isn't in the model list.";
            return;
        }

        var c = _services.Config;
        c.WebFetchEnabled = WebFetchEnabled;
        c.Overnight.ShutdownWhenDone = ShutdownWhenDone;
        if (!string.IsNullOrWhiteSpace(NewMeshyKey)) c.MeshyApiKeyProtected = _services.Secrets.Protect(NewMeshyKey);
        c.Providers = Providers.Select(p => p.ToConfig(_services.Secrets)).ToList();
        c.RotationOrder = order;
        c.MaxSteps = Math.Clamp(MaxSteps, 1, 200);
        c.LimitCooldownMinutes = Math.Clamp(LimitCooldownMinutes, 1, 24 * 60);
        c.ExtraAllowedCommands = SplitList(ExtraAllowedCommands.Replace(",", "\n"));
        c.Supervisor.PlannerProviderId = PlannerId.Trim();
        c.Supervisor.WorkerProviderId = WorkerId.Trim();
        c.Supervisor.TestCommand = TestCommand.Trim();
        c.Supervisor.MaxRounds = Math.Clamp(MaxRounds, 1, 20);
        c.Supervisor.CostTiers = SplitList(CostTiers);
        c.Overnight.MaxMinutesPerTask = Math.Clamp(MaxMinutesPerTask, 5, 600);
        c.Overnight.MaxAttemptsPerTask = Math.Clamp(MaxAttemptsPerTask, 1, 10);
        c.Overnight.KeepPcAwake = KeepPcAwake;
        c.Overnight.PushBranches = PushBranches;
        _services.Save();
        Error = null;
        SavedMessage = "Settings saved. Providers updated live.";
        Saved?.Invoke();
        await Task.Delay(TimeSpan.FromSeconds(3));
        SavedMessage = null;
    }
}
