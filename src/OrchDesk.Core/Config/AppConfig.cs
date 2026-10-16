using System.Text.Json;
using System.Text.Json.Serialization;

namespace OrchDesk.Core.Config;

public enum ProviderKind { OpenAiCompatible, ClaudeCli, CodexCli, OpenCodeCli }

public sealed record ModelVerification(bool Verified, bool Hidden, DateTimeOffset CheckedAt, string Reason);

public sealed class ProviderConfig
{
    public string Id { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public ProviderKind Kind { get; set; }
    public bool Enabled { get; set; } = true;
    /// <summary>Marks models on the user's own PC: preferred when online, skipped when the PC is off.</summary>
    public bool IsLocal { get; set; }

    public int ContextLength { get; set; } = 65536;
    public int ModelTtlSeconds { get; set; } = 3600;
    public bool? UseLmStudioLoad { get; set; }
    public Dictionary<string, ModelVerification> ModelVerifications { get; set; } = [];
    public string? AuthStatus { get; set; }
    public List<string> AvailableModels { get; set; } = [];
    public DateTimeOffset? ModelsSyncedAt { get; set; }
    public Dictionary<string, string> ModelBaseUrls { get; set; } = [];

    // OpenAI-compatible
    public string? BaseUrl { get; set; }
    public string? Model { get; set; }
    public string? ApiKeyProtected { get; set; }
    public string? AccessClientIdProtected { get; set; }
    public string? AccessClientSecretProtected { get; set; }

    // CLIs
    public string? Executable { get; set; }
    public string? ClaudeConfigDir { get; set; }
    public string? PermissionMode { get; set; }
}

public sealed class SupervisorConfig
{
    public string PlannerProviderId { get; set; } = "claude-outlook";
    public string WorkerProviderId { get; set; } = "local-pc";
    public string TestCommand { get; set; } = "dotnet test";
    public int MaxRounds { get; set; } = 4;
    /// <summary>Cost router tiers, cheapest first.</summary>
    public List<string> CostTiers { get; set; } = ["local-pc", "opencode-go", "claude-outlook"];
}

public sealed class OvernightConfig
{
    public string StartTime { get; set; } = "01:00";
    public int MaxMinutesPerTask { get; set; } = 90;
    public int MaxAttemptsPerTask { get; set; } = 3;
    public bool KeepPcAwake { get; set; } = true;
    public bool PushBranches { get; set; }
    public bool ShutdownWhenDone { get; set; }
}

public enum WorkMode { Plan, Agent }
public enum WorkspacePermission { Manual, FullAccess }

public sealed class AppConfig
{
    public bool? AgentsPanelOpen { get; set; }
    public Dictionary<string, WorkspacePermission> WorkspacePermissions { get; set; } = [];
    public WorkMode LastWorkMode { get; set; } = WorkMode.Agent;
    public static string WorkspaceKey(string path) => OrchDesk.Core.Tools.WorkspacePath.NormalizeRoot(path).ToUpperInvariant();
    public WorkspacePermission PermissionFor(string path) => WorkspacePermissions.GetValueOrDefault(WorkspaceKey(path));
    public void SetPermission(string path, WorkspacePermission permission) => WorkspacePermissions[WorkspaceKey(path)] = permission;

    public bool PreferFreeModels { get; set; } = false;
    public bool WebFetchEnabled { get; set; } = true;
    public string? MeshyApiKeyProtected { get; set; }
    public List<ProviderConfig> Providers { get; set; } = [];
    /// <summary>Fallback order used by "Auto" mode.</summary>
    public List<string> RotationOrder { get; set; } = ["remote-pc", "claude-outlook", "opencode-go"];
    public int LimitCooldownMinutes { get; set; } = 300;
    public int MaxSteps { get; set; } = 40;
    public bool? ReduceMotion { get; set; }
    public bool FirstRunCompleted { get; set; }
    public string DisplayName { get; set; } = "MW";
    public string Initials { get; set; } = "MW";
    public bool SidebarCollapsed { get; set; }
    public string Theme { get; set; } = "Dark";
    public string? LastWorkspace { get; set; }
    public List<string> RecentWorkspaces { get; set; } = [];
    public string? LastProviderId { get; set; }
    public List<string> ExtraAllowedCommands { get; set; } = [];
    public Dictionary<string, OrchDesk.Core.Orchestration.TeamConfig> WorkspaceTeams { get; set; } = [];
    public OrchDesk.Core.Orchestration.TeamConfig Team { get; set; } = new();
    public SupervisorConfig Supervisor { get; set; } = new();
    public OvernightConfig Overnight { get; set; } = new();

    public static AppConfig CreateDefault()
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return new AppConfig
        {
            Providers =
            [
                new() { Id = "local-pc", DisplayName = "Desktop PC (Qwen)", Kind = ProviderKind.OpenAiCompatible, IsLocal = true,
                        BaseUrl = "http://127.0.0.1:1234/v1", Model = "qwen/qwen3.5-9b" },
                new() { Id = "local-pc-gemma", DisplayName = "LM Studio (Gemma)", Kind = ProviderKind.OpenAiCompatible, IsLocal = true, BaseUrl = "http://127.0.0.1:1234/v1", Model = "google/gemma-4-12b-qat" },
                new() { Id = "remote-pc", DisplayName = "Remote PC (orch-relay)", Kind = ProviderKind.OpenAiCompatible, BaseUrl = "", Model = "qwen/qwen3.5-9b", Enabled = false },
                new() { Id = "ollama", DisplayName = "Ollama local", Kind = ProviderKind.OpenAiCompatible, IsLocal = true, BaseUrl = "http://127.0.0.1:11434/v1", Model = "", Enabled = false },
                new() { Id = "claude-outlook", DisplayName = "Claude (Outlook)", Kind = ProviderKind.ClaudeCli,
                        Executable = "claude", ClaudeConfigDir = Path.Combine(home, ".claude-outlook"), PermissionMode = "acceptEdits" },
                new() { Id = "claude-gmail", DisplayName = "Claude (Gmail)", Kind = ProviderKind.ClaudeCli,
                        Executable = "claude", ClaudeConfigDir = Path.Combine(home, ".claude-gmail"), PermissionMode = "acceptEdits" },
                new() { Id = "opencode-go", DisplayName = "OpenCode Go", Kind = ProviderKind.OpenAiCompatible,
                        BaseUrl = "https://opencode.ai/inference/go/v1", Model = "glm-5.2" },
                new() { Id = "opencode-cli", DisplayName = "OpenCode CLI", Kind = ProviderKind.OpenCodeCli, Executable = "opencode", Enabled = false },
                new() { Id = "codex", DisplayName = "Codex CLI", Kind = ProviderKind.CodexCli, Executable = "codex", Enabled = false },
            ],
            RotationOrder = ["remote-pc", "claude-outlook", "opencode-go", "local-pc", "claude-gmail"],
        };
    }
}

/// <summary>Loads/saves %APPDATA%\OrchDesk\config.json. Secrets inside are already DPAPI-protected strings.</summary>
public sealed class ConfigStore(string? directory = null, string? filePath = null) : IDisposable
{
    public static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    public string Directory { get; } = filePath is not null ? Path.GetDirectoryName(Path.GetFullPath(filePath))! : directory ??
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "OrchDesk");
    public string FilePath { get; } = filePath is not null ? Path.GetFullPath(filePath) : Path.Combine(directory ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "OrchDesk"), "config.json");

    public static ConfigStore FromArgs(string[] args)
    {
        var index = Array.FindIndex(args, a => a.Equals("--config", StringComparison.OrdinalIgnoreCase));
        if (index < 0) return new();
        if (index + 1 >= args.Length || args[index + 1].StartsWith("--")) throw new ArgumentException("--config requires a path.");
        return new(filePath: args[index + 1]);
    }

    private FileSystemWatcher? _watcher;
    public event Action? Changed;
    public void Watch()
    {
        if (_watcher is not null) return;
        System.IO.Directory.CreateDirectory(Directory);
        _watcher = new FileSystemWatcher(Directory, Path.GetFileName(FilePath)) { NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName };
        _watcher.Changed += (_, _) => Changed?.Invoke();
        _watcher.Created += (_, _) => Changed?.Invoke();
        _watcher.Renamed += (_, _) => Changed?.Invoke();
        _watcher.EnableRaisingEvents = true;
    }
    public void Dispose() => _watcher?.Dispose();

    public AppConfig Load()
    {
        if (!File.Exists(FilePath)) return AppConfig.CreateDefault();
        try {
            var config = JsonSerializer.Deserialize<AppConfig>(File.ReadAllText(FilePath), Json) ?? AppConfig.CreateDefault();
            foreach (var provider in config.Providers)
                if (provider.Id == "opencode-go" && provider.BaseUrl?.TrimEnd('/') == "https://opencode.ai/zen/go/v1") provider.BaseUrl = "https://opencode.ai/inference/go/v1";
            return config;
        }
        catch (JsonException)
        {
            File.Copy(FilePath, FilePath + ".broken", overwrite: true); // keep the bad file for inspection
            return AppConfig.CreateDefault();
        }
    }

    public void Save(AppConfig config)
    {
        System.IO.Directory.CreateDirectory(Directory);
        var tmp = FilePath + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(config, Json));
        File.Move(tmp, FilePath, overwrite: true); // atomic replace: a crash never leaves half a file
        Changed?.Invoke();
    }
}
