using System.Text.Json;
using OrchDesk.Core.Config;
using OrchDesk.Core.Integrations;
using OrchDesk.Core.Agent;
using OrchDesk.Core.Models;
using OrchDesk.Core.Permissions;
using OrchDesk.Core.Tools;
namespace OrchDesk.Core.Verification;
public sealed record SmokeResult(string Provider, string Status, string Detail);
public static class IntegrationSmokeRunner
{
    public static string IntegrationSummary(AppConfig config) => $"Claude: {config.Providers.Where(p => p.Kind == ProviderKind.ClaudeCli).Sum(p => p.AvailableModels.Count)} models; OpenCode: {config.Providers.Where(p => p.Id == "opencode-go").Sum(p => p.AvailableModels.Count)} models";

    public static async Task<List<SmokeResult>> RunAsync(AppConfig config, ISecretProtector secrets, HttpClient http, string directory, CancellationToken ct = default)
    {
        Directory.CreateDirectory(directory);
        var results = new List<SmokeResult>();
        Console.WriteLine(IntegrationSummary(config));
        var factory = new ProviderFactory(secrets, http);
        var tester = new ConnectionTester(http, secrets);
        foreach (var c in config.Providers) {
            if (!c.Enabled || (c.Kind == ProviderKind.OpenAiCompatible && (string.IsNullOrWhiteSpace(c.BaseUrl) || string.IsNullOrWhiteSpace(c.Model) || (c.Id == "opencode-go" && string.IsNullOrWhiteSpace(c.ApiKeyProtected)))) || (c.Kind != ProviderKind.OpenAiCompatible && OrchDesk.Core.Providers.CliRunner.FindOnPath(c.Executable ?? "") is null)) {
                results.Add(new(c.Id, "skip", "Not configured or disabled")); continue;
            }
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct); timeout.CancelAfter(TimeSpan.FromMinutes(3));
            try {
                var connection = await tester.TestAsync(c, timeout.Token);
                if (!connection.Ok) { results.Add(new(c.Id, "fail", "Connection test failed")); continue; }
                bool passed = false;
                int attempt;
                for (attempt = 1; attempt <= 3; attempt++)
                {
                    try
                    {
                        var workspace = Path.Combine(directory, c.Id.Replace('/', '_').Replace('\\', '_') + "-" + Guid.NewGuid().ToString("N"));
                        Directory.CreateDirectory(workspace);
                        var provider = factory.Create(c, temperature: 0)!;
                        var loop = new AgentLoop(new ToolRegistry().Add(new WriteFileTool()).Add(new ReadFileTool()), new PermissionGate(new PermissionPolicy(), null), new AgentLoopOptions { MaxSteps = 6 });
                        bool failed = false, text = false, wrote = false, read = false;
                        var messages = new List<ChatMessage> { ChatMessage.System("You are a deterministic tool-use smoke test. On the first turn make exactly one write_file tool call with JSON arguments {\"path\":\"smoke.txt\",\"content\":\"ORCHDESK_OK\"}. No prose or reasoning before the call. After its result make exactly one read_file call with JSON arguments {\"path\":\"smoke.txt\"}. After the read result reply ORCHDESK_OK. Never combine calls or run commands."), ChatMessage.User("Use your file tool to create smoke.txt containing exactly ORCHDESK_OK, then read it back using your file tool and report its contents. Do not run commands.") };
                        await foreach (var ev in loop.RunAsync(provider, messages, new ToolContext(workspace), timeout.Token)) {
                            failed |= ev is ErrorEvent or ProviderUnavailable or UsageLimitHit;
                            text |= ev is TextDelta { Text.Length: > 0 };
                            wrote |= ev is ToolCallResult { Name: "write_file", IsError: false };
                            read |= ev is ToolCallResult { Name: "read_file", IsError: false };
                        }
                        var file = Path.Combine(workspace, "smoke.txt");
                        var pass = !failed && text && ((wrote && read) || provider.IsAgentic) && File.Exists(file) && (await File.ReadAllTextAsync(file, timeout.Token)).Trim() == "ORCHDESK_OK";
                        if (pass) { passed = true; break; }
                    }
                    catch (Exception) when (!timeout.IsCancellationRequested) { /* Retry with a fresh transcript and workspace. */ }
                }
                results.Add(new(c.Id, passed ? "pass" : "fail", passed ? $"Connection, chat and file tool round trip passed (attempt {attempt}/3)" : "Chat/tool round trip failed (3 attempts)"));
            } catch (Exception) { results.Add(new(c.Id, "fail", "Provider failed or timed out")); }
            await File.WriteAllTextAsync(Path.Combine(directory, "smoketest.json"), JsonSerializer.Serialize(results, ConfigStore.Json), CancellationToken.None);
        }
        results.Add(new("web-fetch", config.WebFetchEnabled ? (await tester.TestWebAsync(true, ct)).Ok ? "pass" : "fail" : "skip", "GET example.com"));
        results.Add(new("meshy", string.IsNullOrWhiteSpace(config.MeshyApiKeyProtected) ? "skip" : (await tester.TestMeshyAsync(config.MeshyApiKeyProtected, ct)).Ok ? "pass" : "fail", "Optional API connection"));
        await File.WriteAllTextAsync(Path.Combine(directory, "smoketest.json"), JsonSerializer.Serialize(results, ConfigStore.Json), CancellationToken.None);
        return results;
    }
}
