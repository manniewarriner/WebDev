using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
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
    [ObservableProperty] private bool _detailsExpanded;
    public bool IsConnected => Enabled && ModelsSyncedAt is not null;
    public string ConnectActionLabel => IsConnected ? "Manage" : "Connect";
    public bool NeedsConnection => !IsConnected;
    public bool IsOpenCode => Id.StartsWith("opencode", StringComparison.OrdinalIgnoreCase);
    public string FriendlyName => DisplayName;
    [RelayCommand] private void Manage() => DetailsExpanded = !DetailsExpanded;
    public void Disconnect(bool removeKey = false)
    {
        Enabled = false; AvailableModels.Clear(); ModelBaseUrls.Clear(); ModelsSyncedAt = null;
        if (removeKey && IsOpenCode) { _apiKeyProtected = null; OnPropertyChanged(nameof(KeyState)); }
        DetailsExpanded = false; RefreshConnection();
    }
    public string Badge => IsClaude ? "C" : "O";
    public string Subtitle => IsClaude ? "Claude Code · Subscription account" : "OpenCode · Go & Zen models";
    public string ConnectionState => Original.AuthStatus == "Sign-in needed" ? "Sign-in needed" : !Enabled ? "Not connected" : ModelsSyncedAt is not null ? "Connected" : "Needs login";
    public string SyncSummary => ModelsSyncedAt is { } synced
        ? $"Synced {Math.Max(0, (int)(DateTimeOffset.Now - synced).TotalMinutes)}m ago · {AvailableModels.Count} models ? {Original.ModelVerifications.Count(p=>p.Value.Hidden)} hidden"
        : "Not synced yet · Connect to discover models";
    public void RefreshConnection()
    {
        OnPropertyChanged(nameof(IsConnected));
        OnPropertyChanged(nameof(ConnectActionLabel));
        OnPropertyChanged(nameof(NeedsConnection));
        OnPropertyChanged(nameof(ConnectionState));
        OnPropertyChanged(nameof(SyncSummary));
    }
    partial void OnEnabledChanged(bool value) => RefreshConnection();
    [ObservableProperty] private string _status = "Not tested";
    [ObservableProperty] private bool _testing;
    [ObservableProperty] private string _id;
    [ObservableProperty, NotifyPropertyChangedFor(nameof(FriendlyName))] private string _displayName;
    [ObservableProperty, NotifyPropertyChangedFor(nameof(IsHttp), nameof(IsClaude), nameof(IsCli))] private ProviderKind _kind;
    [ObservableProperty] private bool _enabled;
    [ObservableProperty] private bool _isLocal;
    [ObservableProperty] private string? _baseUrl;
    [ObservableProperty, NotifyPropertyChangedFor(nameof(FriendlyName))] private string? _model;
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
            ContextLength = Original.ContextLength, ModelTtlSeconds = Original.ModelTtlSeconds, UseLmStudioLoad = Original.UseLmStudioLoad, ModelVerifications = new(Original.ModelVerifications), AuthStatus=Original.AuthStatus,
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

