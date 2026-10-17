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

    // Imported from the original build's Endpoints: the secrets.bin key id, the /models URL override and the model id filter.
    public string? KeyId { get; set; }
    public string? ModelsUrl { get; set; }
    public string? ModelPattern { get; set; }
    public bool? NativeTools { get; set; }
    [JsonExtensionData] public Dictionary<string, JsonElement>? Extra { get; set; }
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
    /// <summary>
    /// The nearest explicit setting for this folder or one of its parents wins, so "Full access" on a folder covers its subfolders
    /// and nothing else. Folders with no setting (and no parent setting) are Manual ("Ask first").
    /// </summary>
    public WorkspacePermission PermissionFor(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return WorkspacePermission.Manual;
        for (string? dir = OrchDesk.Core.Tools.WorkspacePath.NormalizeRoot(path); !string.IsNullOrEmpty(dir); dir = Path.GetDirectoryName(dir))
            if (WorkspacePermissions.TryGetValue(WorkspaceKey(dir), out var permission)) return permission;
        return WorkspacePermission.Manual;
    }
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

    // ───── Settings carried over from the original OrchDesk build (same key names, so both builds can read one config.json) ─────
    public List<EndpointConfig> Endpoints { get; set; } = [];
    public double MinimumFreeRamGb { get; set; } = 2;
    public int ShellTimeoutSeconds { get; set; } = 120;
    public int OutputLimit { get; set; } = 65536;
    public string TestCommand { get; set; } = "dotnet test";
    /// <summary>Mirror of <see cref="Theme"/> under the original build's key.</summary>
    public bool LightTheme { get => Theme == "Light"; set => Theme = value ? "Light" : "Dark"; }
    /// <summary>
    /// The original build's single global mode (Plan/Manual/Auto). Kept verbatim for round-trips; it never grants per-folder
    /// full access here. Folders use <see cref="WorkspacePermissions"/> (default Manual).
    /// </summary>
    public string Permission { get; set; } = "Manual";
    public string Project { get; set; } = "";
    public int SupervisorMaxRuns { get; set; } = 5;
    public int SupervisorMaxMinutes { get; set; } = 60;
    public string BrainModel { get; set; } = "claude-opus-5-5";
    public string IntakeProvider { get; set; } = "local";
    public string IntakeModel { get; set; } = "";
    public List<TierConfig> Ladder { get; set; } = [];
    public bool NightShift { get; set; }
    public List<TierConfig> NightLadder { get; set; } = [];
    [JsonIgnore] public List<TierConfig> ActiveLadder => NightShift && NightLadder.Count > 0 ? NightLadder : Ladder;
    public int MaxTasks { get; set; } = 6;
    public string VerifyCommand { get; set; } = "auto";
    public int VerifyTimeoutSeconds { get; set; } = 300;
    public int MaxModelCalls { get; set; } = 80;
    public int CallTimeoutMinutes { get; set; } = 20;
    public int TransientRetries { get; set; } = 1;
    public int RepoMapChars { get; set; } = 4000;
    public bool ArchitectSplit { get; set; } = true;
    public bool RoleRules { get; set; } = true;
    public bool LearnedRouting { get; set; } = true;
    // Skills (SKILL.md folders) are guidance text only. ~/.orchdesk/skills always loads; the project folder and ~/.claude/skills are opt-in.
    public bool Skills { get; set; } = true;
    public bool SkillsFromProject { get; set; }
    public bool SkillsFromClaude { get; set; }
    public bool ClaudeSkillsEnabledOnce { get; set; }
    // Persistent memory notes added to prompts (the remember tool still asks first).
    public bool Memory { get; set; } = true;
    // Unattended (scheduled/remote) runs stop starting once today's estimated tokens reach this (0 = no ceiling).
    public long DailyTokenCeiling { get; set; } = 2_000_000;
    // Telegram remote channel: long polling only, one paired private chat.
    public bool TelegramEnabled { get; set; }
    public bool TelegramNotifyJobDone { get; set; } = true;
    public long TelegramChatId { get; set; }
    public bool RemoteAllowAuto { get; set; }
    public int RemoteMaxPerHour { get; set; } = 20;
    public int MaxParallel { get; set; } = 3;
    public int AttemptsPerTier { get; set; } = 1;
    public int MaxRevisionRounds { get; set; } = 1;
    public bool OpusReviewsEveryAttempt { get; set; }
    public string MeshyKeyId { get; set; } = "meshy";
    public string LastModel { get; set; } = "";

    /// <summary>Any key this build does not know (for example one added by a newer original build) survives load + save unchanged.</summary>
    [JsonExtensionData] public Dictionary<string, JsonElement>? Extra { get; set; }

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
                // From the original build. "-free" models stay blocked by ProviderFactory, so OpenCode Free is off unless a paid model is set.
                new() { Id = "opencode-free", DisplayName = "OpenCode Free", Kind = ProviderKind.OpenAiCompatible, BaseUrl = LegacyDefaults.OpenCodeFreeUrl, Model = "", KeyId = "opencode-free",
                        ModelsUrl = LegacyDefaults.OpenCodeFreeModels, ModelPattern = LegacyDefaults.OpenCodeFreePattern, Enabled = false },
                new() { Id = "gemini", DisplayName = "Gemini", Kind = ProviderKind.OpenAiCompatible, BaseUrl = LegacyDefaults.GeminiUrl, Model = "", KeyId = "gemini",
                        ModelPattern = LegacyDefaults.GeminiPattern, Enabled = false },
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
        // The original build writes PascalCase keys; both spellings load.
        PropertyNameCaseInsensitive = true,
    };

    /// <summary>Looks up secrets.bin entries (key id → present?). Supplied by the app; tests leave it null.</summary>
    public Func<string, bool>? HasSecret { get; set; }
    /// <summary>Set when the file on disk is still in the original build's format; the first save writes a one-time backup first.</summary>
    public bool LoadedLegacyFormat { get; private set; }

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
            var text = File.ReadAllText(FilePath);
            var config = JsonSerializer.Deserialize<AppConfig>(text, Json) ?? AppConfig.CreateDefault();
            if (System.Text.Json.Nodes.JsonNode.Parse(text) is System.Text.Json.Nodes.JsonObject raw)
            {
                LoadedLegacyFormat = LegacyConfigImport.IsLegacyFormat(raw);
                LegacyConfigImport.Apply(config, raw, HasSecret);
            }
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
        BackupLegacyOnce();
        var tmp = FilePath + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(config, Json));
        File.Move(tmp, FilePath, overwrite: true); // atomic replace: a crash never leaves half a file
        Changed?.Invoke();
    }

    public const string BackupPrefix = ".pre-merged-";

    /// <summary>
    /// Before this build first rewrites a config.json that is still in the original build's format, the file is copied once to
    /// config.json.pre-merged-&lt;timestamp&gt;. An existing backup is never overwritten and no second backup is made.
    /// </summary>
    private void BackupLegacyOnce()
    {
        if (!File.Exists(FilePath)) return;
        if (System.IO.Directory.EnumerateFiles(Directory, Path.GetFileName(FilePath) + BackupPrefix + "*").Any()) { LoadedLegacyFormat = false; return; }
        bool legacy;
        try { legacy = System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(FilePath)) is System.Text.Json.Nodes.JsonObject raw && LegacyConfigImport.IsLegacyFormat(raw); }
        catch (JsonException) { legacy = false; }
        if (!legacy && !LoadedLegacyFormat) return;
        File.Copy(FilePath, FilePath + BackupPrefix + DateTime.Now.ToString("yyyyMMdd-HHmmss"), overwrite: false);
        LoadedLegacyFormat = false;
    }
}
