using System.Diagnostics;
using System.Net.Http.Headers;
using System.Text.Json;
using OrchDesk.Core.Config;
using OrchDesk.Core.Providers;
using OrchDesk.Core.Tools;

namespace OrchDesk.Core.Integrations;

public interface IClaudeAuthRunner
{
    Task<bool> IsLoggedInAsync(ProviderConfig profile, CancellationToken ct);
}

public sealed class ClaudeAuthRunner : IClaudeAuthRunner
{
    public static ProcessStartInfo CreateAuthStartInfo(string executable, string directory) =>
        CliRunner.CreateStartInfo(executable, ["auth", "status", "--json"], null,
            new ClaudeCliProvider("", "", new(directory)).BuildEnvironment());

    public async Task<bool> IsLoggedInAsync(ProviderConfig profile, CancellationToken ct)
    {
        var executable = CliRunner.FindOnPath(profile.Executable ?? "claude");
        if (executable is null || string.IsNullOrWhiteSpace(profile.ClaudeConfigDir)) return false;
        var result = await ProcessRunner.RunAsync(CreateAuthStartInfo(executable, profile.ClaudeConfigDir), TimeSpan.FromSeconds(20), ct);
        if (result.ExitCode != 0 || result.TimedOut) return false;
        using var json = JsonDocument.Parse(result.Output);
        return json.RootElement.TryGetProperty("loggedIn", out var loggedIn) && loggedIn.ValueKind == JsonValueKind.True
            && json.RootElement.TryGetProperty("authMethod", out var method) && method.GetString() is "oauth_token" or "claude.ai";
    }
}

public sealed record ModelSyncResult(bool Ok, string Message);

public sealed class IntegrationModelSync(HttpClient http, ISecretProtector secrets, IClaudeAuthRunner claude)
{
    public async Task<ModelSyncResult> RunAsync(ProviderConfig provider, bool sync = true, CancellationToken ct = default)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(45));
        try
        {
            List<string> models;
            var modelBaseUrls = new Dictionary<string, string>();
            if (provider.Kind == ProviderKind.ClaudeCli)
            {
                if (!await claude.IsLoggedInAsync(provider, timeout.Token)) return new(false, "Claude unavailable or profile not logged in with a subscription.");
                models = ["sonnet", "opus", "haiku"];
            }
            else
            {
                var key = secrets.TryUnprotect(provider.ApiKeyProtected);
                if (string.IsNullOrWhiteSpace(key)) return new(false, "Enter an OpenCode API key.");
                var go = provider.BaseUrl?.TrimEnd('/') ?? "https://opencode.ai/inference/go/v1";
                if (go != "https://opencode.ai/inference/go/v1" && go != "https://opencode.ai/zen/v1") return new(false, "Use an OpenCode Go or Zen endpoint.");
                models = [];
                foreach (var endpoint in new[] { "https://opencode.ai/inference/go/v1", "https://opencode.ai/zen/v1" })
                {
                    using var request = new HttpRequestMessage(HttpMethod.Get, endpoint + "/models");
                    request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", key);
                    using var response = await http.SendAsync(request, timeout.Token);
                    if (!response.IsSuccessStatusCode) return new(false, $"OpenCode model request failed (HTTP {(int)response.StatusCode}).");
                    using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(timeout.Token));
                    foreach (var item in json.RootElement.GetProperty("data").EnumerateArray())
                        if (item.TryGetProperty("id", out var id) && id.GetString() is { Length: > 0 } value)
                        {
                            models.Add(value);
                            modelBaseUrls.TryAdd(value, endpoint);
                        }
                }
                models = models.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToList();
                if (models.Count == 0) return new(false, "OpenCode returned no models.");
            }
            if (sync) { provider.AvailableModels = models; provider.ModelBaseUrls = modelBaseUrls; provider.ModelsSyncedAt = DateTimeOffset.UtcNow; }
            return new(true, sync ? $"Synced {models.Count} models" : "Connected");
        }
        catch (OperationCanceledException) { return new(false, "Integration check cancelled or timed out."); }
        catch (Exception) { return new(false, "Integration check failed; check authentication and connection."); }
    }
}
