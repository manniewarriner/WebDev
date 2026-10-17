using System.IO;
using System.Net.Http;
using OrchDesk.Core.Accounts;
using OrchDesk.Core.Agent;
using OrchDesk.Core.Config;
using OrchDesk.Core.Overnight;
using OrchDesk.Core.Permissions;
using OrchDesk.Core.Providers;
using OrchDesk.Core.Tools;

namespace OrchDesk.App.Services;

/// <summary>
/// Everything the views share: config, the live providers, the rotation chain, tools and the overnight queue.
/// Rebuild() is called after Settings are saved so new endpoints/keys take effect without restarting.
/// </summary>
public sealed class AppServices
{
    private static readonly HttpClient Http = new(new SocketsHttpHandler { ConnectTimeout = TimeSpan.FromSeconds(4) }) { Timeout = Timeout.InfiniteTimeSpan }; // streams can run long; requests use their own tokens

    public OrchDesk.Core.Orchestration.ModelUsageTracker Usage { get; private set; } = null!;
    public OrchDesk.Core.Knowledge.MemoryStore Memory { get; }
    public OrchDesk.Core.Automation.ScheduleStore Schedules { get; }
    public OrchDesk.Core.Telemetry.UsageLog UsageLog { get; }
    public OrchDesk.Core.Telemetry.RoutingStats Routing { get; }
    public OrchDesk.Core.Dashboard.TaskStore Tasks { get; }
    public OrchDesk.Core.Dashboard.ReportStore Reports { get; }
    public OrchDesk.Core.Research.ResearchClient Research { get; }
    public ConfigStore Store { get; }
    public ISecretProtector Secrets { get; }
    /// <summary>secrets.bin shared with the original build (telegram, opencode-go, opencode-free, gemini, meshy ...).</summary>
    public SecretStore SecretFile { get; }
    public ToolRegistry Tools { get; private set; } = ToolRegistry.CreateDefault();
    public AppConfig Config { get; private set; }
    public IReadOnlyList<IModelProvider> Providers { get; private set; } = [];
    public AccountRotator Rotator { get; private set; } = new([]);
    public OvernightQueueStore Queue { get; }
    public string ReportsDir { get; }

    public event Action? Rebuilt;
    public Func<bool>? AutomationBusy { get; set; }

    public bool ChatOnly { get; }
    public bool Unattended { get; }
    private readonly IReadOnlyList<IModelProvider>? _providerOverride;
    public bool IsScreenshot { get; }
    public bool UseDemo { get; }
    public Task StartupSync { get; private set; } = Task.CompletedTask;

    public AppServices(bool screenshot = false, ConfigStore? store = null, bool? demo = null, bool chatOnly = false, IReadOnlyList<IModelProvider>? providers = null, bool unattended = false)
    {
        ChatOnly = chatOnly; Unattended = unattended;
        _providerOverride = providers;
        Store = store ?? new();
        var protector = SecretStoreProtector.ForDirectory(Store.Directory);
        Secrets = protector; SecretFile = protector.Store!;
        Store.HasSecret ??= SecretFile.Has;
        string? DataFile(string name) => screenshot ? null : Path.Combine(Store.Directory, name);
        Research = new(() => SecretFile.Get("gemini"), Http);
        Memory = new(DataFile("memory.json")); Schedules = new(DataFile("schedules.json"));
        UsageLog = new(DataFile("usage.jsonl")); Routing = new(DataFile("routing-stats.json"));
        Tasks = new(DataFile("tasks.json")); Reports = new(DataFile("reports.json"));
        Usage = new(screenshot ? null : Store.Directory);
        IsScreenshot = screenshot;
        var useFixtures = UseDemo = screenshot && (demo ?? store is null);
        Config = useFixtures ? AppConfig.CreateDefault() : Store.Load();
        if (useFixtures)
        {
            // Display-only fixtures: this instance never watches or saves user configuration.
            foreach (var provider in Config.Providers.Where(p => p.Kind == ProviderKind.ClaudeCli || p.Id == "opencode-go"))
                if (provider.AvailableModels.Count == 0)
                {
                    provider.AvailableModels = provider.Kind == ProviderKind.ClaudeCli
                        ? ["sonnet", "opus", "haiku"] : ["glm-5.2", "kimi-k2.5", "minimax-m2.5"];
                    provider.ModelsSyncedAt = DateTimeOffset.Now.AddMinutes(-2);
                }
        }
        if (useFixtures) { var disconnected = Config.Providers.First(p => p.Id == "claude-outlook"); disconnected.Enabled = false; disconnected.AvailableModels.Clear(); disconnected.ModelsSyncedAt = null; }
        Queue = new OvernightQueueStore(Path.Combine(Store.Directory, "overnight"));
        ReportsDir = Path.Combine(Store.Directory, "reports");
        Rebuild();
        if (chatOnly) { if (providers is null) StartupSync = SyncModelsAsync(); return; }
        if (providers is not null) return;
        if (screenshot) { if (!UseDemo) StartupSync = SyncModelsAsync(); return; }
        Store.Changed += () => System.Windows.Application.Current.Dispatcher.BeginInvoke(new Action(() => {
            try { Config = Store.Load(); Rebuild(); } catch (IOException) { }
        }));
        Store.Watch();
        StartupSync = SyncModelsAsync();
    }

    public async Task RecheckModelsAsync() { await StartupSync; foreach(var p in Config.Providers) p.ModelVerifications.Clear(); await SyncModelsAsync(); }

    public async Task EnsureModelVerificationAsync()
    {
        await StartupSync;
        if (_providerOverride is not null || UseDemo) return;
        if (Config.Providers.Where(p=>p.Enabled && p.Kind==ProviderKind.OpenAiCompatible).Any(p=>p.ModelVerifications.Values.Any(v=>v.CheckedAt<DateTimeOffset.UtcNow.AddDays(-1))))
            await SyncModelsAsync();
    }

    private async Task SyncModelsAsync()
    {
        var config = Config;
        var sync = new OrchDesk.Core.Integrations.IntegrationModelSync(Http, Secrets, new OrchDesk.Core.Integrations.ClaudeAuthRunner());
        var results = await OrchDesk.Core.Integrations.StartupModelSync.RunAsync(config, sync);
        var factory = new ProviderFactory(Secrets, Http);
        await Task.WhenAll(config.Providers.Where(p=>p.Enabled && p.Kind==ProviderKind.OpenAiCompatible).Select(p=>OrchDesk.Core.Integrations.ModelProbe.RunAsync(p,m=>factory.Create(p,m),rateLimited:(provider,delay)=>Usage.Backoff(provider,delay))));
        if (ReferenceEquals(config, Config)) Save();
    }

    public void Rebuild()
    {
        Tools = ChatOnly ? new ToolRegistry() : ToolRegistry.CreateDefault(Config.WebFetchEnabled);
        if (!ChatOnly) {
            if (Config.Memory) Tools.Add(new RememberTool(Memory));
            var meshy = new MeshyClient(() => SecretFile.Get(Config.MeshyKeyId), Http);
            Tools.Add(new GenerateImageTool(meshy)).Add(new Generate3DTool(meshy));
        }
        var factory = new ProviderFactory(Secrets, Http);
        Providers = _providerOverride ?? factory.CreateAll(Config).Select(Track).ToList();
        // Keep limit history across rebuilds so a settings change doesn't forget a provider is cooling down.
        var old = Rotator;
        Rotator = new AccountRotator(ProviderFactory.InRotationOrder(Config, Providers), TimeSpan.FromMinutes(Config.LimitCooldownMinutes));
        foreach (var a in Rotator.Accounts)
            if (old.Find(a.Provider.Id) is { } prev) { a.LastLimitHit = prev.LastLimitHit; a.LastLimitMessage = prev.LastLimitMessage; a.Health = prev.Health; a.LastOfflineAt = prev.LastOfflineAt; a.LastChecked = prev.LastChecked; }
        Rebuilt?.Invoke();
    }

    public AccountRotator CreateRunRotator(ModelPickerOption? selected)
    {
        if (_providerOverride is not null || ChatOnly || selected?.Model is null) return Rotator;
        var factory = new ProviderFactory(Secrets, Http);
        var providers = Config.Providers.Select(c => factory.Create(c, c.Id == selected.Id ? selected.Model : null)).OfType<IModelProvider>().Select(Track).ToList();
        var result = new AccountRotator(ProviderFactory.InRotationOrder(Config, providers), TimeSpan.FromMinutes(Config.LimitCooldownMinutes));
        foreach (var account in result.Accounts)
            if (Rotator.Find(account.Provider.Id) is { } previous) { account.Health = previous.Health; account.LastLimitHit = previous.LastLimitHit; account.LastOfflineAt = previous.LastOfflineAt; }
        result.Changed += () =>
        {
            foreach (var account in result.Accounts)
                if (Rotator.Find(account.Provider.Id) is { } shared)
                {
                    shared.Health = account.Health;
                    shared.LastLimitHit = account.LastLimitHit;
                    shared.LastLimitMessage = account.LastLimitMessage;
                    shared.LastOfflineAt = account.LastOfflineAt;
                    shared.LastChecked = account.LastChecked;
                }
        };
        return result;
    }

    public IReadOnlyList<IModelProvider> AutoModels(string? workspace = null)
    {
        if (_providerOverride is not null) return _providerOverride;
        var factory = new ProviderFactory(Secrets, Http);
        var team = workspace is null ? Config.Team : Config.WorkspaceTeams.GetValueOrDefault(AppConfig.WorkspaceKey(workspace)) ?? Config.Team;
        return Config.Providers.Where(c => c.Enabled && (c.Kind != ProviderKind.ClaudeCli || c.AuthStatus != "Sign-in needed")).SelectMany(c =>
            (c.Kind == ProviderKind.ClaudeCli ? new[] { "opus", "haiku", c.Id == team.PmProvider ? team.PmModel : "opus" }.Distinct().ToArray() : c.AvailableModels.Concat(c.ModelVerifications.Where(v=>v.Value.Verified && !v.Value.Hidden).Select(v=>v.Key)).Append(c.Model ?? "").Where(m=>!string.IsNullOrWhiteSpace(m)).Distinct().ToArray())
            .Where(m => !Config.NightShift || Config.NightLadder.Count == 0 || c.Id == team.PmProvider || Config.NightLadder.Any(t => LegacyDefaults.ProviderId(t.Provider) == c.Id && (string.IsNullOrWhiteSpace(t.Model) || t.Model == m)))
            .Where(m => c.Kind != ProviderKind.OpenAiCompatible || (c.ModelVerifications.TryGetValue(m, out var check) && check.Verified && !check.Hidden))
              .Where(m => !m.Contains("embed", StringComparison.OrdinalIgnoreCase) && !m.EndsWith("-free", StringComparison.OrdinalIgnoreCase))
            .Select(m => factory.Create(c, m))).OfType<IModelProvider>().Select(Track).ToList();
    }

    public IReadOnlyList<IModelProvider> LegacyCostTiers()
    {
        var factory = new ProviderFactory(Secrets, Http);
        return Config.ActiveLadder.Select(t => {
            var id = LegacyDefaults.ProviderId(t.Provider);
            var provider = Config.Providers.FirstOrDefault(p => p.Id == id);
            return provider is null ? null : factory.Create(provider, string.IsNullOrWhiteSpace(t.Model) ? null : t.Model);
        }).OfType<IModelProvider>().Select(Track).ToList();
    }

    private IModelProvider Track(IModelProvider provider) => new OrchDesk.Core.Telemetry.UsageLoggingProvider(provider, UsageLog, Config.MinimumFreeRamGb);

    public void Save()
    {
        if (IsScreenshot) return;
        Store.Save(Config);
        Rebuild();
    }

    public PermissionPolicy CreatePolicy()
    {
        var p = new PermissionPolicy { Unattended = Unattended, PermissionResolver = Config.PermissionFor };
        p.ExtraAllowedCommandPrefixes.AddRange(Config.ExtraAllowedCommands);
        return p;
    }

    public AgentLoop CreateLoop(PermissionGate gate) =>
        new(Tools, gate, new AgentLoopOptions { MaxSteps = Config.MaxSteps });

    public OvernightRunner CreateOvernightRunner() =>
        new(Queue, Tools, Rotator, Config.Overnight, Config.Supervisor, Config.MaxSteps, ReportsDir);
}
