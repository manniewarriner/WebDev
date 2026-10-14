using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OrchDesk.App.Services;
using OrchDesk.Core.Config;

namespace OrchDesk.App.ViewModels;

/// <summary>
/// Editable copy of one provider. New secrets typed into the password boxes are held here in memory only until
/// Save, when they're DPAPI-encrypted into config.json. Existing secrets are never shown back.
/// </summary>
public sealed partial class ProviderEdit : ObservableObject
{
    public ProviderEdit(ProviderConfig c)
    {
        _id = c.Id; _displayName = c.DisplayName; _kind = c.Kind; _enabled = c.Enabled; _isLocal = c.IsLocal;
        _baseUrl = c.BaseUrl; _model = c.Model; _executable = c.Executable; _claudeConfigDir = c.ClaudeConfigDir;
        _permissionMode = c.PermissionMode;
        Original = c;
    }

    public ProviderConfig Original { get; }
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

    public string? NewApiKey { get; set; }
    public string? NewAccessClientId { get; set; }
    public string? NewAccessClientSecret { get; set; }
    public bool ClearSecrets { get; set; }

    public bool IsHttp => Kind == ProviderKind.OpenAiCompatible;
    public bool IsClaude => Kind == ProviderKind.ClaudeCli;
    public bool IsCli => Kind != ProviderKind.OpenAiCompatible;
    public string KeyState => string.IsNullOrEmpty(Original.ApiKeyProtected) ? "No key saved" : "A key is saved (type to replace)";

    public ProviderConfig ToConfig(ISecretProtector secrets)
    {
        string? Keep(string? newValue, string? old) =>
            ClearSecrets ? null : string.IsNullOrWhiteSpace(newValue) ? old : secrets.Protect(newValue.Trim());
        return new ProviderConfig
        {
            Id = Id.Trim(), DisplayName = DisplayName.Trim(), Kind = Kind, Enabled = Enabled, IsLocal = IsLocal,
            BaseUrl = BaseUrl?.Trim(), Model = Model?.Trim(), Executable = Executable?.Trim(),
            ClaudeConfigDir = ClaudeConfigDir?.Trim(), PermissionMode = PermissionMode?.Trim(),
            ApiKeyProtected = Keep(NewApiKey, Original.ApiKeyProtected),
            AccessClientIdProtected = Keep(NewAccessClientId, Original.AccessClientIdProtected),
            AccessClientSecretProtected = Keep(NewAccessClientSecret, Original.AccessClientSecretProtected),
        };
    }
}

public sealed partial class SettingsViewModel : ObservableObject
{
    private readonly AppServices _services;

    public ObservableCollection<ProviderEdit> Providers { get; }
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

    public event Action? Saved;

    public SettingsViewModel(AppServices services)
    {
        _services = services;
        var c = services.Config;
        Providers = new(c.Providers.Select(p => new ProviderEdit(p)));
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
    private void AddProvider()
    {
        var p = new ProviderEdit(new ProviderConfig
        {
            Id = $"provider-{Providers.Count + 1}", DisplayName = "New model", Kind = ProviderKind.OpenAiCompatible,
            BaseUrl = "http://127.0.0.1:1234/v1",
        });
        Providers.Add(p);
        Selected = p;
    }

    [RelayCommand]
    private void RemoveProvider()
    {
        if (Selected is null) return;
        var i = Providers.IndexOf(Selected);
        Providers.Remove(Selected);
        Selected = Providers.ElementAtOrDefault(Math.Max(0, i - 1));
    }

    private static List<string> SplitList(string s) =>
        s.Split([',', '\n', '\r'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();

    [RelayCommand]
    private void Save()
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
        Saved?.Invoke();
    }
}
