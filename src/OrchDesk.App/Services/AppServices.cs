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
    private static readonly HttpClient Http = new() { Timeout = Timeout.InfiniteTimeSpan }; // streams can run long; requests use their own tokens

    public ConfigStore Store { get; } = new();
    public ISecretProtector Secrets { get; } = new DpapiSecretProtector();
    public ToolRegistry Tools { get; private set; } = ToolRegistry.CreateDefault();
    public AppConfig Config { get; private set; }
    public IReadOnlyList<IModelProvider> Providers { get; private set; } = [];
    public AccountRotator Rotator { get; private set; } = new([]);
    public OvernightQueueStore Queue { get; }
    public string ReportsDir { get; }

    public event Action? Rebuilt;

    public bool IsScreenshot { get; }

    public AppServices(bool screenshot = false)
    {
        IsScreenshot = screenshot;
        Config = screenshot ? AppConfig.CreateDefault() : Store.Load();
        if (screenshot)
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
        Queue = new OvernightQueueStore(Path.Combine(Store.Directory, "overnight"));
        ReportsDir = Path.Combine(Store.Directory, "reports");
        Rebuild();
        if (screenshot) return;
        Store.Changed += () => System.Windows.Application.Current.Dispatcher.BeginInvoke(new Action(() => {
            try { Config = Store.Load(); Rebuild(); } catch (IOException) { }
        }));
        Store.Watch();
    }

    public void Rebuild()
    {
        Tools = ToolRegistry.CreateDefault(Config.WebFetchEnabled);
        var factory = new ProviderFactory(Secrets, Http);
        Providers = factory.CreateAll(Config);
        // Keep limit history across rebuilds so a settings change doesn't forget a provider is cooling down.
        var old = Rotator;
        Rotator = new AccountRotator(ProviderFactory.InRotationOrder(Config, Providers), TimeSpan.FromMinutes(Config.LimitCooldownMinutes));
        foreach (var a in Rotator.Accounts)
            if (old.Find(a.Provider.Id) is { } prev) { a.LastLimitHit = prev.LastLimitHit; a.LastLimitMessage = prev.LastLimitMessage; a.Health = prev.Health; a.LastOfflineAt = prev.LastOfflineAt; a.LastChecked = prev.LastChecked; }
        Rebuilt?.Invoke();
    }

    public AccountRotator CreateRunRotator(ModelPickerOption? selected)
    {
        if (selected?.Model is null) return Rotator;
        var factory = new ProviderFactory(Secrets, Http);
        var providers = Config.Providers.Select(c => factory.Create(c, c.Id == selected.Id ? selected.Model : null)).OfType<IModelProvider>().ToList();
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

    public void Save()
    {
        if (IsScreenshot) return;
        Store.Save(Config);
        Rebuild();
    }

    public PermissionPolicy CreatePolicy()
    {
        var p = new PermissionPolicy();
        p.ExtraAllowedCommandPrefixes.AddRange(Config.ExtraAllowedCommands);
        return p;
    }

    public AgentLoop CreateLoop(PermissionGate gate) =>
        new(Tools, gate, new AgentLoopOptions { MaxSteps = Config.MaxSteps });

    public OvernightRunner CreateOvernightRunner() =>
        new(Queue, Tools, Rotator, Config.Overnight, Config.Supervisor, Config.MaxSteps, ReportsDir);
}
