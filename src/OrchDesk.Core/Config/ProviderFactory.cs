using OrchDesk.Core.Providers;

namespace OrchDesk.Core.Config;

/// <summary>Turns saved provider settings into live provider objects.</summary>
public sealed class ProviderFactory(ISecretProtector secrets, HttpClient http)
{
    // Stable per app run so OpenCode sees one session (its docs ask clients for a steady x-opencode-session).
    private static readonly string SessionId = Guid.NewGuid().ToString("N");

    public IModelProvider? Create(ProviderConfig c, string? modelOverride = null, double? temperature = null)
    {
        if (modelOverride is not null)
        {
            c = System.Text.Json.JsonSerializer.Deserialize<ProviderConfig>(System.Text.Json.JsonSerializer.Serialize(c))!;
            c.Model = modelOverride;
        }
        if (!c.Enabled || (c.Kind == ProviderKind.OpenAiCompatible && c.Model?.EndsWith("-free", StringComparison.OrdinalIgnoreCase) == true)) return null;
        var baseUrl = c.BaseUrl;
        if (c.Id == "opencode-go" && c.Model is not null && c.ModelBaseUrls.TryGetValue(c.Model, out var endpoint)
            && endpoint is "https://opencode.ai/inference/go/v1" or "https://opencode.ai/zen/v1") baseUrl = endpoint;
        switch (c.Kind)
        {
            case ProviderKind.OpenAiCompatible:
                var headers = new Dictionary<string, string>();
                if (secrets.TryUnprotect(c.AccessClientIdProtected) is { } cfId && secrets.TryUnprotect(c.AccessClientSecretProtected) is { } cfSecret)
                {
                    headers["CF-Access-Client-Id"] = cfId;
                    headers["CF-Access-Client-Secret"] = cfSecret;
                }
                if (c.BaseUrl?.Contains("opencode.ai", StringComparison.OrdinalIgnoreCase) == true)
                    {
                    headers["x-opencode-session"] = SessionId;
                    headers["User-Agent"] = "OrchDesk/0.1";
                }
                return new OpenAiCompatProvider(c.Id, c.DisplayName,
                    new OpenAiCompatOptions(baseUrl ?? "", c.Model ?? "", secrets.TryUnprotect(c.ApiKeyProtected), headers, temperature, LmStudioLoad: c.UseLmStudioLoad ?? (c.Id != "ollama" && (c.IsLocal || c.Id == "remote-pc" || c.BaseUrl?.Contains(":1234") == true)), ContextLength: c.ContextLength, TtlSeconds: c.ModelTtlSeconds),
                    c.IsLocal, http);

            case ProviderKind.ClaudeCli:
                return new ClaudeCliProvider(c.Id, c.DisplayName, new ClaudeCliOptions(
                    c.ClaudeConfigDir ?? "", c.Executable ?? "claude", string.IsNullOrWhiteSpace(c.Model) ? null : c.Model,
                    c.PermissionMode ?? "acceptEdits"));

            case ProviderKind.CodexCli:
                // `codex exec -` reads the prompt from stdin; workspace-write keeps it inside the folder.
                var codexArgs = new List<string> { "exec", "--sandbox", "workspace-write", "--skip-git-repo-check" };
                if (!string.IsNullOrWhiteSpace(c.Model)) codexArgs.AddRange(["--model", c.Model]);
                codexArgs.Add("-");
                return new GenericCliProvider(c.Id, c.DisplayName, c.Executable ?? "codex", codexArgs);

            case ProviderKind.OpenCodeCli:
                var ocArgs = new List<string> { "run" };
                if (!string.IsNullOrWhiteSpace(c.Model)) ocArgs.AddRange(["--model", c.Model]);
                return new GenericCliProvider(c.Id, c.DisplayName, c.Executable ?? "opencode", ocArgs);

            default:
                return null;
        }
    }

    public static string? AutoModel(AppConfig config, ProviderConfig provider)
    {
        return provider.Model?.EndsWith("-free", StringComparison.OrdinalIgnoreCase) == true ? provider.AvailableModels.FirstOrDefault(m => !m.EndsWith("-free", StringComparison.OrdinalIgnoreCase)) : provider.Model;
    }

    public List<IModelProvider> CreateAll(AppConfig config) =>
        config.Providers.Select(c => CreateAuto(config, c)).OfType<IModelProvider>().ToList();

    private IModelProvider? CreateAuto(AppConfig config, ProviderConfig c)
    {
        var preferred = Create(c, AutoModel(config, c));
        var configured = Create(c);
        return preferred is not null && configured is not null && preferred.ModelId != configured.ModelId
            ? new PreferredModelProvider(preferred, configured) : preferred;
    }

    /// <summary>Providers in the configured fallback order (unknown/disabled ids are skipped).</summary>
    public static List<IModelProvider> InRotationOrder(AppConfig config, IReadOnlyList<IModelProvider> all) =>
        config.RotationOrder.Select(id => all.FirstOrDefault(p => p.Id == id)).OfType<IModelProvider>()
            .Concat(all).DistinctBy(p => p.Id).ToList();
}
