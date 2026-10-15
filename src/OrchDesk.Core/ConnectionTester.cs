using System.Diagnostics;
using System.Net.Http.Headers;
using OrchDesk.Core.Config;
using OrchDesk.Core.Models;
using OrchDesk.Core.Providers;
using OrchDesk.Core.Tools;

namespace OrchDesk.Core.Integrations;

public sealed record ConnectionTestResult(bool Ok, string Message, long ElapsedMs);
public sealed class ConnectionTester(HttpClient http, ISecretProtector secrets)
{
    public async Task<ConnectionTestResult> TestAsync(ProviderConfig config, CancellationToken ct = default)
    {
        return await Measure(async token => {
            if (config.Kind == ProviderKind.ClaudeCli) return await new ClaudeAuthRunner().IsLoggedInAsync(config, token);
            if (config.Kind != ProviderKind.OpenAiCompatible) {
                var exe = CliRunner.FindOnPath(config.Executable ?? (config.Kind == ProviderKind.ClaudeCli ? "claude" : "codex"));
                if (exe is null) return false;
                // Presence only: do not set CLAUDE_CONFIG_DIR or inspect any profile.
                var env = CliRunner.PaidApiEnvVars.ToDictionary(k => k, _ => (string?)null);
                var result = await ProcessRunner.RunAsync(CliRunner.CreateStartInfo(exe, ["--version"], null, env), TimeSpan.FromSeconds(20), token);
                return result.ExitCode == 0 && !result.TimedOut;
            }
            var enabled = config.Enabled; config.Enabled = true;
            IModelProvider? provider;
            try { provider = new ProviderFactory(secrets, http).Create(config); }
            finally { config.Enabled = enabled; }
            if (provider is null) return false;
            bool text = false, failed = false;
            await foreach (var ev in provider.StreamChatAsync(new ChatRequest([ChatMessage.User("Reply with OK.")], []), token)) {
                text |= ev is TextDelta { Text.Length: > 0 };
                failed |= ev is ErrorEvent or ProviderUnavailable or UsageLimitHit;
            }
            return text && !failed;
        }, ct);
    }
    public Task<ConnectionTestResult> TestWebAsync(bool enabled, CancellationToken ct = default) => Measure(async token => {
        if (!enabled) return false;
        using var response = await http.GetAsync("https://example.com", token);
        return response.IsSuccessStatusCode;
    }, ct);
    public Task<ConnectionTestResult> TestMeshyAsync(string? protectedKey, CancellationToken ct = default) => Measure(async token => {
        var key = secrets.TryUnprotect(protectedKey);
        if (string.IsNullOrWhiteSpace(key)) return false;
        using var request = new HttpRequestMessage(HttpMethod.Get, "https://api.meshy.ai");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", key);
        using var response = await http.SendAsync(request, token);
        return response.IsSuccessStatusCode;
    }, ct);
    private static async Task<ConnectionTestResult> Measure(Func<CancellationToken, Task<bool>> test, CancellationToken ct)
    {
        var watch = Stopwatch.StartNew();
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(60));
        try { var ok = await test(timeout.Token); return new(ok, ok ? "Connected" : "Failed: check configuration, authentication and endpoint", watch.ElapsedMilliseconds); }
        catch (OperationCanceledException) { return new(false, "Connection test cancelled or timed out", watch.ElapsedMilliseconds); }
        catch (Exception) { return new(false, "Connection failed; check the endpoint and credentials", watch.ElapsedMilliseconds); }
    }
}
