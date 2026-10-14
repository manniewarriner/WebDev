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
    public ToolRegistry Tools { get; } = ToolRegistry.CreateDefault();
    public AppConfig Config { get; private set; }
    public IReadOnlyList<IModelProvider> Providers { get; private set; } = [];
    public AccountRotator Rotator { get; private set; } = new([]);
    public OvernightQueueStore Queue { get; }
    public string ReportsDir { get; }

    public event Action? Rebuilt;

    public AppServices()
    {
        Config = Store.Load();
        Queue = new OvernightQueueStore(Path.Combine(Store.Directory, "overnight"));
        ReportsDir = Path.Combine(Store.Directory, "reports");
        Rebuild();
    }

    public void Rebuild()
    {
        var factory = new ProviderFactory(Secrets, Http);
        Providers = factory.CreateAll(Config);
        // Keep limit history across rebuilds so a settings change doesn't forget a provider is cooling down.
        var old = Rotator;
        Rotator = new AccountRotator(ProviderFactory.InRotationOrder(Config, Providers), TimeSpan.FromMinutes(Config.LimitCooldownMinutes));
        foreach (var a in Rotator.Accounts)
            if (old.Find(a.Provider.Id) is { } prev) { a.LastLimitHit = prev.LastLimitHit; a.LastLimitMessage = prev.LastLimitMessage; a.Health = prev.Health; }
        Rebuilt?.Invoke();
    }

    public void Save()
    {
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
