using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace OrchDesk.Core.Config;

/// <summary>An OpenAI-compatible endpoint as saved by the original OrchDesk build (config.json "Endpoints").</summary>
public sealed class EndpointConfig
{
    public string Name { get; set; } = "Local · LM Studio / Bionic";
    public string Url { get; set; } = "http://127.0.0.1:1234/v1";
    public string Model { get; set; } = "local-model";
    public string KeyId { get; set; } = "local";
    public bool Local { get; set; } = true;
    public bool NativeTools { get; set; } = true;
    // Optional absolute URL for GET /models when it is not <Url>/models (OpenCode 2.0 serves lists at /inference/v1/models).
    public string ModelsUrl { get; set; } = "";
    // Optional regex: only model ids that match are offered.
    public string ModelPattern { get; set; } = "";
    [JsonExtensionData] public Dictionary<string, JsonElement>? Extra { get; set; }
    public override string ToString() => $"{Name} / {Model}";
}

/// <summary>One rung of the cheap-first ladder. Provider is an endpoint key id ("local", "opencode-go", ...) or "claude" / "codex".</summary>
public sealed class TierConfig
{
    public string Name { get; set; } = "";
    public string Provider { get; set; } = "";
    // Blank = first model the provider reports.
    public string Model { get; set; } = "";
    [JsonExtensionData] public Dictionary<string, JsonElement>? Extra { get; set; }
    public override string ToString() => Name;
}

public static class LegacyDefaults
{
    public const string LocalChatPattern = "^(?!.*embed)";
    public const string OpenCodeGoUrl = "https://opencode.ai/inference/go/openai/v1", OpenCodeGoModels = "https://opencode.ai/inference/go/v1/models", OpenCodeGoPattern = "^(glm|kimi|deepseek|mimo|hy|longcat|step|beam|space-bunny|big-pickle)";
    public const string OpenCodeFreeUrl = "https://opencode.ai/inference/openai/v1", OpenCodeFreeModels = "https://opencode.ai/inference/v1/models", OpenCodeFreePattern = "(-free$|^big-pickle$)";
    public const string GeminiUrl = "https://generativelanguage.googleapis.com/v1beta/openai", GeminiPattern = "gemini";

    public static EndpointConfig GeminiEndpoint() => new() { Name = "Gemini", Url = GeminiUrl, ModelPattern = GeminiPattern, Model = "", KeyId = "gemini", Local = false };
    public static List<EndpointConfig> Endpoints() =>
    [
        new() { ModelPattern = LocalChatPattern },
        new() { Name = "OpenCode Go", Url = OpenCodeGoUrl, ModelsUrl = OpenCodeGoModels, ModelPattern = OpenCodeGoPattern, Model = "", KeyId = "opencode-go", Local = false },
        new() { Name = "OpenCode Free", Url = OpenCodeFreeUrl, ModelsUrl = OpenCodeFreeModels, ModelPattern = OpenCodeFreePattern, Model = "", KeyId = "opencode-free", Local = false },
        GeminiEndpoint(),
    ];
    public static List<TierConfig> Ladder() =>
    [
        new() { Name = "Local", Provider = "local" },
        new() { Name = "OpenCode Free", Provider = "opencode-free" },
        new() { Name = "OpenCode Go", Provider = "opencode-go" },
        new() { Name = "Claude Haiku 5.5", Provider = "claude", Model = "claude-haiku-5-5" },
        new() { Name = "Claude Sonnet 5.5", Provider = "claude", Model = "claude-sonnet-5-5" },
    ];

    /// <summary>Endpoint key ids and ladder provider names used by the original build, mapped to provider ids of this build.</summary>
    public static string ProviderId(string legacy) => legacy.Trim().ToLowerInvariant() switch
    {
        "local" => "local-pc",
        "claude" => "claude-outlook",
        "codex" => "codex",
        var other => other,
    };
}

public static class ModelPicker
{
    // Intake only rewrites a request, so the smallest/fastest chat model is the right default. Returns null when nothing looks small.
    public static string? Small(IEnumerable<string> ids) => ids
        .Where(id => !id.Contains("embed", StringComparison.OrdinalIgnoreCase))
        .FirstOrDefault(id => Regex.IsMatch(id, @"(e2b|e4b|nano|mini|tiny|small|lite|flash|(^|[^\d])[1-4](\.\d)?b\b)", RegexOptions.IgnoreCase));
}

public static class NightLadderBuilder
{
    // Local chat models smallest to largest (by the "7b" style size in the id; unknown sizes go last), at most `max`, then the Claude tiers.
    public static List<TierConfig> Build(IEnumerable<string> localIds, IEnumerable<TierConfig> ladder, int max = 4)
    {
        double Size(string id) { var m = Regex.Match(id, @"(?<![\d.])(\d+(?:\.\d+)?)\s*b\b", RegexOptions.IgnoreCase); return m.Success ? double.Parse(m.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture) : double.MaxValue; }
        var local = localIds.Where(id => !id.Contains("embed", StringComparison.OrdinalIgnoreCase)).Distinct().OrderBy(Size).ThenBy(id => id, StringComparer.OrdinalIgnoreCase).ToList();
        if (local.Count > max) local = Enumerable.Range(0, max).Select(i => local[(int)Math.Round(i * (local.Count - 1) / (double)(max - 1))]).Distinct().ToList();
        return local.Select(id => new TierConfig { Name = "Local · " + id, Provider = "local", Model = id })
            .Concat(ladder.Where(t => t.Provider.Equals("claude", StringComparison.OrdinalIgnoreCase)).Select(t => new TierConfig { Name = t.Name, Provider = t.Provider, Model = t.Model })).ToList();
    }
}

/// <summary>
/// Reads a config.json written by the original OrchDesk build (PascalCase keys, Endpoints/Ladder, global Permission) into this build's
/// AppConfig without dropping anything. Only keys that are absent from the file are derived; nothing the file states is overwritten.
/// </summary>
public static class LegacyConfigImport
{
    public static bool Has(JsonObject raw, string key) => raw.Any(p => p.Key.Equals(key, StringComparison.OrdinalIgnoreCase));

    /// <summary>True for a file that has never been saved by this build (no providers list).</summary>
    public static bool IsLegacyFormat(JsonObject raw) => !Has(raw, "providers") && (Has(raw, "endpoints") || Has(raw, "ladder") || Has(raw, "permission"));

    public static void Apply(AppConfig config, JsonObject raw, Func<string, bool>? hasSecret = null)
    {
        hasSecret ??= _ => false;
        MigrateEndpoints(config);
        if (config.Providers.Count == 0) config.Providers = MergeProviders(config.Endpoints, hasSecret);

        // Provider fields that point at secrets.bin when no protected key of this build is saved.
        foreach (var provider in config.Providers)
            if (string.IsNullOrEmpty(provider.ApiKeyProtected) && provider.KeyId is { Length: > 0 } keyId && hasSecret(keyId))
                provider.ApiKeyProtected = SecretStoreProtector.Ref(keyId);
        if (string.IsNullOrEmpty(config.MeshyApiKeyProtected) && config.MeshyKeyId.Length > 0 && hasSecret(config.MeshyKeyId))
            config.MeshyApiKeyProtected = SecretStoreProtector.Ref(config.MeshyKeyId);

        if (!Has(raw, "theme") && Has(raw, "lightTheme")) config.Theme = config.LightTheme ? "Light" : "Dark";
        if (!Has(raw, "lastWorkspace") && Has(raw, "project") && config.Project.Length > 0) config.LastWorkspace = config.Project;
        if (!Has(raw, "lastProviderId") && Has(raw, "lastModel")) config.LastProviderId = LastProvider(config.LastModel);
        if (!Has(raw, "supervisor") && Has(raw, "testCommand")) config.Supervisor.TestCommand = config.TestCommand;
        if (!Has(raw, "supervisor") && Has(raw, "ladder") && config.Ladder.Count > 0)
            config.Supervisor.CostTiers = config.Ladder.Select(t => LegacyDefaults.ProviderId(t.Provider)).Distinct().ToList();
        if (!Has(raw, "team") && Has(raw, "maxParallel")) config.Team.MaxConcurrency = Math.Clamp(config.MaxParallel, 1, 8);
        if (!Has(raw, "team") && Has(raw, "brainModel") && config.BrainModel.Contains("opus", StringComparison.OrdinalIgnoreCase)) config.Team.PmModel = "opus";
        if (!Has(raw, "team")) {
            config.Team.MaxTasks = Math.Max(1, config.MaxTasks);
            config.Team.MaxModelCalls = Math.Max(1, config.MaxModelCalls);
            config.Team.MaxRevisionRounds = Math.Max(0, config.MaxRevisionRounds);
            config.Team.OpusReviewsEveryAttempt = config.OpusReviewsEveryAttempt;
            config.Team.ArchitectSplit = config.ArchitectSplit;
        }
        // The original build's global Permission applied to every folder. It is deliberately NOT turned into per-folder full access:
        // every folder starts at the normal "Ask first" (Manual) default and full access must be granted per folder.
    }

    private static string? LastProvider(string lastModel)
    {
        if (string.IsNullOrWhiteSpace(lastModel) || lastModel == "orchestra") return null; // Orchestra == Auto model
        var keyId = lastModel.Split('|')[0];
        return LegacyDefaults.ProviderId(keyId);
    }

    // Move saved pre-2.0 OpenCode URLs (/zen/v1, /zen/go/v1) onto the protocol-specific inference paths (same rule as the original build).
    public static void MigrateEndpoints(AppConfig config)
    {
        foreach (var e in config.Endpoints)
        {
            if (e.Local && e.ModelPattern.Length == 0) e.ModelPattern = LegacyDefaults.LocalChatPattern;
            var url = e.Url.TrimEnd('/');
            if (url.Equals("https://opencode.ai/zen/go/v1", StringComparison.OrdinalIgnoreCase)) { e.Url = LegacyDefaults.OpenCodeGoUrl; e.ModelsUrl = LegacyDefaults.OpenCodeGoModels; if (e.ModelPattern.Length == 0) e.ModelPattern = LegacyDefaults.OpenCodeGoPattern; }
            else if (url.Equals("https://opencode.ai/zen/v1", StringComparison.OrdinalIgnoreCase)) { e.Url = LegacyDefaults.OpenCodeFreeUrl; e.ModelsUrl = LegacyDefaults.OpenCodeFreeModels; if (e.ModelPattern.Length == 0) e.ModelPattern = LegacyDefaults.OpenCodeFreePattern; }
        }
        if (config.Endpoints.Count > 0 && !config.Endpoints.Any(e => e.KeyId.Equals("gemini", StringComparison.OrdinalIgnoreCase))) config.Endpoints.Add(LegacyDefaults.GeminiEndpoint());
    }

    /// <summary>This build's default providers, with the user's saved endpoints folded in (matched by key id, unknown ones added).</summary>
    public static List<ProviderConfig> MergeProviders(IEnumerable<EndpointConfig> endpoints, Func<string, bool> hasSecret)
    {
        var providers = AppConfig.CreateDefault().Providers;
        foreach (var e in endpoints)
        {
            var id = LegacyDefaults.ProviderId(e.KeyId);
            var p = providers.FirstOrDefault(x => x.Id.Equals(id, StringComparison.OrdinalIgnoreCase));
            if (p is null) { p = new ProviderConfig { Id = id, DisplayName = e.Name, Kind = ProviderKind.OpenAiCompatible, BaseUrl = e.Url, Enabled = hasSecret(e.KeyId) || e.Local }; providers.Add(p); }
            p.KeyId = e.KeyId; p.ModelsUrl = e.ModelsUrl; p.ModelPattern = e.ModelPattern; p.NativeTools = e.NativeTools;
            if (e.Local) { p.IsLocal = true; if (!string.IsNullOrWhiteSpace(e.Url)) p.BaseUrl = e.Url; }
            if (!string.IsNullOrWhiteSpace(e.Model) && e.Model != "local-model") p.Model = e.Model;
            // opencode-go keeps this build's base URL (its model sync validates the endpoint); gemini turns on once its key exists.
            if (id == "gemini") p.Enabled = hasSecret(e.KeyId);
        }
        return providers;
    }

    /// <summary>Lower-cases every property name, so a PascalCase file and a camelCase save can be compared key by key.</summary>
    public static JsonNode? NormalizeKeys(JsonNode? node) => node switch
    {
        JsonObject o => new JsonObject(o.Select(p => KeyValuePair.Create(p.Key.ToLowerInvariant(), NormalizeKeys(p.Value)))),
        JsonArray a => new JsonArray(a.Select(NormalizeKeys).ToArray()),
        null => null,
        _ => node.DeepClone(),
    };
}
