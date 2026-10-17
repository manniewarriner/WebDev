using System.Text.Json.Nodes;
using OrchDesk.Core.Config;
using OrchDesk.Core.Permissions;

namespace OrchDesk.Tests;

/// <summary>The merged build must start against the user data the original OrchDesk build wrote, without losing anything.</summary>
public sealed class BaselineCompatTests
{
    private static string Fixture => File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "baseline-config.json"));

    private static ConfigStore StoreWith(TempDir dir, string json, Func<string, bool>? hasSecret = null)
    {
        File.WriteAllText(Path.Combine(dir.Path, "config.json"), json);
        return new ConfigStore(dir.Path) { HasSecret = hasSecret };
    }

    [Fact]
    public void Baseline_config_keeps_every_key_and_value_after_load_and_save()
    {
        using var dir = new TempDir();
        using var store = StoreWith(dir, Fixture);
        store.Save(store.Load());

        var original = (JsonObject)LegacyConfigImport.NormalizeKeys(JsonNode.Parse(Fixture))!;
        var saved = (JsonObject)LegacyConfigImport.NormalizeKeys(JsonNode.Parse(File.ReadAllText(store.FilePath)))!;
        foreach (var (key, value) in original)
        {
            Assert.True(saved.ContainsKey(key), "missing key " + key);
            Assert.True(JsonNode.DeepEquals(value, saved[key]), $"{key}: {value?.ToJsonString()} != {saved[key]?.ToJsonString()}");
        }
    }

    [Fact]
    public void Unknown_keys_survive_a_round_trip()
    {
        using var dir = new TempDir();
        var json = JsonNode.Parse(Fixture)!.AsObject();
        json["FutureSetting"] = new JsonObject { ["Nested"] = 7 };
        json["Endpoints"]![0]!["ExtraField"] = "kept";
        using var store = StoreWith(dir, json.ToJsonString());
        store.Save(store.Load());
        var saved = JsonNode.Parse(File.ReadAllText(store.FilePath))!.AsObject();
        Assert.Equal(7, saved["FutureSetting"]!["Nested"]!.GetValue<int>());
        Assert.Equal("kept", saved["endpoints"]![0]!["ExtraField"]!.GetValue<string>());
        // A second load+save of the new format keeps them too.
        store.Save(store.Load());
        Assert.Equal(7, JsonNode.Parse(File.ReadAllText(store.FilePath))!["FutureSetting"]!["Nested"]!.GetValue<int>());
    }

    [Fact]
    public void Missing_providers_are_defaulted_and_merged_with_baseline_endpoints()
    {
        using var dir = new TempDir();
        using var store = StoreWith(dir, Fixture, id => id is "opencode-go" or "gemini");
        var config = store.Load();
        foreach (var id in new[] { "claude-outlook", "codex", "opencode-go", "opencode-free", "local-pc", "gemini" })
            Assert.Contains(config.Providers, p => p.Id == id);
        var local = config.Providers.Single(p => p.Id == "local-pc");
        Assert.Equal("local", local.KeyId); Assert.Equal("http://127.0.0.1:1234/v1", local.BaseUrl); Assert.True(local.IsLocal);
        var go = config.Providers.Single(p => p.Id == "opencode-go");
        Assert.Equal(SecretStoreProtector.Ref("opencode-go"), go.ApiKeyProtected);
        Assert.Equal("https://opencode.ai/inference/go/v1/models", go.ModelsUrl);
        Assert.True(config.Providers.Single(p => p.Id == "gemini").Enabled);
        Assert.Null(config.Providers.Single(p => p.Id == "opencode-free").ApiKeyProtected); // no secret saved for it
        Assert.Null(config.LastProviderId); // "orchestra" == Auto model
        Assert.Equal(["local-pc", "opencode-free", "opencode-go", "claude-outlook"], config.Supervisor.CostTiers);
        Assert.Equal("C:\\Users\\mannie\\Documents", config.LastWorkspace);
        Assert.Equal(4, config.Endpoints.Count);
        Assert.Equal(5, config.Ladder.Count); Assert.Equal(6, config.NightLadder.Count);
        Assert.Same(config.NightLadder, config.ActiveLadder);
    }

    [Fact]
    public void Saved_providers_are_never_redefaulted()
    {
        using var dir = new TempDir();
        var json = JsonNode.Parse(Fixture)!.AsObject();
        json["providers"] = new JsonArray(new JsonObject { ["id"] = "only-one", ["kind"] = "OpenAiCompatible" });
        using var store = StoreWith(dir, json.ToJsonString());
        Assert.Equal("only-one", Assert.Single(store.Load().Providers).Id);
    }

    [Fact]
    public void First_save_writes_one_backup_of_the_baseline_file()
    {
        using var dir = new TempDir();
        using var store = StoreWith(dir, Fixture);
        var config = store.Load();
        Assert.True(store.LoadedLegacyFormat);
        store.Save(config); store.Save(config);
        var backup = Assert.Single(Directory.GetFiles(dir.Path, "config.json.pre-merged-*"));
        Assert.Equal(Fixture, File.ReadAllText(backup));
        Assert.Contains("\"providers\"", File.ReadAllText(store.FilePath));
    }

    [Fact]
    public void Branch_format_config_gets_no_backup()
    {
        using var dir = new TempDir(); using var store = new ConfigStore(dir.Path);
        store.Save(AppConfig.CreateDefault()); store.Save(store.Load());
        Assert.Empty(Directory.GetFiles(dir.Path, "config.json.pre-merged-*"));
    }

    [Fact]
    public void Telegram_and_remote_settings_are_preserved()
    {
        using var dir = new TempDir();
        using var store = StoreWith(dir, Fixture);
        store.Save(store.Load());
        var config = store.Load();
        Assert.True(config.TelegramEnabled);
        Assert.Equal(123456789L, config.TelegramChatId);
        Assert.True(config.RemoteAllowAuto);
        Assert.Equal(20, config.RemoteMaxPerHour);
        Assert.Equal(2_000_000, config.DailyTokenCeiling);
        Assert.True(config.Skills); Assert.True(config.SkillsFromClaude); Assert.True(config.Memory);
        Assert.Equal("meshy", config.MeshyKeyId);
    }

    [Fact]
    public void Baseline_global_auto_permission_never_grants_folder_full_access()
    {
        using var dir = new TempDir();
        using var store = StoreWith(dir, Fixture);
        var config = store.Load();
        Assert.Equal("Auto", config.Permission); // kept verbatim for the original build
        Assert.Empty(config.WorkspacePermissions);
        Assert.Equal(WorkspacePermission.Manual, config.PermissionFor(config.Project));
        Assert.Equal(WorkspacePermission.Manual, config.PermissionFor(Path.Combine(config.Project, "Any", "Sub")));
        store.Save(config);
        Assert.Equal("Auto", JsonNode.Parse(File.ReadAllText(store.FilePath))!["permission"]!.GetValue<string>());
    }

    [Fact]
    public void Full_access_applies_only_to_the_folder_and_its_subfolders()
    {
        using var dir = new TempDir();
        var granted = Path.Combine(dir.Path, "Granted"); var sibling = Path.Combine(dir.Path, "Sibling");
        Directory.CreateDirectory(Path.Combine(granted, "sub")); Directory.CreateDirectory(sibling);
        var config = new AppConfig(); config.SetPermission(granted, WorkspacePermission.FullAccess);
        Assert.Equal(WorkspacePermission.FullAccess, config.PermissionFor(granted));
        Assert.Equal(WorkspacePermission.FullAccess, config.PermissionFor(Path.Combine(granted, "sub", "deeper")));
        Assert.Equal(WorkspacePermission.Manual, config.PermissionFor(sibling));
        Assert.Equal(WorkspacePermission.Manual, config.PermissionFor(dir.Path));
        Assert.Equal(WorkspacePermission.Manual, config.PermissionFor(granted + "Suffix")); // a name prefix is not a subfolder

        // A run rooted at the parent: edits inside the granted folder go straight through, edits elsewhere still ask.
        var policy = new PermissionPolicy { WorkspaceRoot = dir.Path, WorkspacePermission = config.PermissionFor(dir.Path), PermissionResolver = config.PermissionFor };
        Assert.Equal(Verdict.Allow, policy.Evaluate("write_file", new() { ["path"] = "Granted/sub/a.txt", ["content"] = "x" }).Verdict);
        Assert.Equal(Verdict.Ask, policy.Evaluate("write_file", new() { ["path"] = "Sibling/a.txt", ["content"] = "x" }).Verdict);
        Assert.Equal(Verdict.Ask, policy.Evaluate("run_command", new() { ["command"] = "dotnet test" }).Verdict);

        // An explicit Manual on a subfolder overrides the parent's full access.
        config.SetPermission(Path.Combine(granted, "sub"), WorkspacePermission.Manual);
        Assert.Equal(WorkspacePermission.Manual, config.PermissionFor(Path.Combine(granted, "sub", "x")));
    }

    [Fact]
    public async Task Full_access_edits_are_recorded_in_the_change_log_for_undo()
    {
        using var dir = new TempDir();
        var config = new AppConfig(); config.SetPermission(dir.Path, WorkspacePermission.FullAccess);
        var log = new ChangeLog();
        var policy = new PermissionPolicy { WorkspacePermission = config.PermissionFor(dir.Path), PermissionResolver = config.PermissionFor };
        var loop = new OrchDesk.Core.Agent.AgentLoop(OrchDesk.Core.Tools.ToolRegistry.CreateDefault(), new PermissionGate(policy, null)) { Changes = log };
        var (_, isError) = await loop.ExecuteToolAsync(new OrchDesk.Core.Models.ToolCall("1", "write_file", "{\"path\":\"a.txt\",\"content\":\"hi\"}"), new OrchDesk.Core.Tools.ToolContext(dir.Path), default);
        Assert.False(isError);
        var entry = Assert.Single(log.Entries);
        entry.Undo();
        Assert.False(File.Exists(Path.Combine(dir.Path, "a.txt")));
    }

    [DpapiFact]
    public void Secrets_bin_round_trips_in_a_temp_directory_and_is_only_rewritten_on_change()
    {
        if (!OperatingSystem.IsWindows()) return;
        using var dir = new TempDir();
        var store = new SecretStore(dir.Path);
        Assert.False(store.Has("telegram"));
        Assert.True(store.Set("telegram", "123456:test-token-value"));
        Assert.True(store.Set("gemini", "test-gemini"));
        var written = File.GetLastWriteTimeUtc(store.FilePath);
        var bytes = File.ReadAllBytes(store.FilePath);
        Assert.False(store.Set("telegram", "123456:test-token-value")); // unchanged: file untouched
        Assert.Equal(bytes, File.ReadAllBytes(store.FilePath));
        Assert.Equal(written, File.GetLastWriteTimeUtc(store.FilePath));

        // A fresh reader (as the original build would be) sees the same DPAPI CurrentUser, no-entropy JSON dictionary.
        var raw = System.Security.Cryptography.ProtectedData.Unprotect(File.ReadAllBytes(store.FilePath), null, System.Security.Cryptography.DataProtectionScope.CurrentUser);
        var map = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, string>>(raw)!;
        Assert.Equal("test-gemini", map["gemini"]);
        var again = new SecretStore(dir.Path);
        Assert.Equal("123456:test-token-value", again.Get("telegram"));
        Assert.True(again.Set("gemini", "")); Assert.False(new SecretStore(dir.Path).Has("gemini"));
        Assert.False(File.Exists(store.FilePath + ".tmp"));

        var protector = SecretStoreProtector.ForDirectory(dir.Path);
        Assert.Equal("123456:test-token-value", protector.TryUnprotect(SecretStoreProtector.Ref("telegram")));
        Assert.Null(protector.TryUnprotect(SecretStoreProtector.Ref("missing")));
        Assert.Equal("[REDACTED] leaked", again.Redact("123456:test-token-value leaked"));
    }

    [Fact]
    public void Theme_and_last_model_map_from_baseline_keys()
    {
        using var dir = new TempDir();
        var json = JsonNode.Parse(Fixture)!.AsObject();
        json["LightTheme"] = true; json["LastModel"] = "opencode-go|https://opencode.ai/inference/go/openai/v1|glm-5.2";
        using var store = StoreWith(dir, json.ToJsonString());
        var config = store.Load();
        Assert.Equal("Light", config.Theme);
        Assert.Equal("opencode-go", config.LastProviderId);
    }
}
