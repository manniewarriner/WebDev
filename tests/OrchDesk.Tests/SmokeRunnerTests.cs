using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using OrchDesk.Core.Config;
using OrchDesk.Core.Verification;
using Xunit;
namespace OrchDesk.Tests;
public class SmokeRunnerTests
{
    private sealed class Secrets : ISecretProtector { public string Protect(string s) => s; public string Unprotect(string s) => s; }
    private sealed class Handler(int failures = 0) : HttpMessageHandler
    {
        private int _turn;
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var message = new JsonObject();
            if (request.Method == HttpMethod.Post)
            {
                var payload = JsonNode.Parse(await request.Content!.ReadAsStringAsync(ct))!;
                if (payload["messages"]![0]!["content"]!.ToString().Contains("deterministic tool-use"))
                    Assert.Equal(0, payload["temperature"]!.GetValue<double>());
                _turn++;
                var step = _turn - failures;
                if (step is 2 or 3)
                {
                    var args = new JsonObject { ["path"] = "smoke.txt" };
                    if (step == 2) args["content"] = "ORCHDESK_OK";
                    message["tool_calls"] = new JsonArray(new JsonObject {
                        ["id"] = "tool-" + _turn,
                        ["function"] = new JsonObject { ["name"] = step == 2 ? "write_file" : "read_file", ["arguments"] = args.ToJsonString() }
                    });
                }
                else message["content"] = "ORCHDESK_OK";
            }
            var body = new JsonObject { ["choices"] = new JsonArray(new JsonObject { ["message"] = message, ["finish_reason"] = "stop" }) };
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json") };
        }
    }
    [Theory]
    [InlineData(1, "pass", "attempt 2/3")]
    [InlineData(2, "pass", "attempt 3/3")]
    [InlineData(3, "fail", "3 attempts")]
    public async Task SmokeRetriesAndReportsAttempt(int failures, string status, string detail)
    {
        using var dir = new TempDir(); using var http = new HttpClient(new Handler(failures));
        var config = new AppConfig { WebFetchEnabled = false, Providers = [new() { Id = "qwen", BaseUrl = "https://provider.test/v1", Model = "qwen" }] };
        var results = await IntegrationSmokeRunner.RunAsync(config, new Secrets(), http, dir.Path);
        var result = results.Single(r => r.Provider == "qwen");
        Assert.Equal(status, result.Status);
        Assert.Contains(detail, result.Detail);
    }

    [Theory]
    [InlineData("local-pc")][InlineData("local-pc-gemma")][InlineData("ollama")][InlineData("remote-pc")][InlineData("opencode-go")]
    public async Task SmokeProvesWriteReadRoundTripAndReportsSkippedIntegrations(string id)
    {
        using var dir = new TempDir(); using var http = new HttpClient(new Handler());
        var config = new AppConfig { WebFetchEnabled = false, Providers = [new() { Id = id, BaseUrl = "https://provider.test/v1", Model = "model", ApiKeyProtected = "key" }, new() { Id = "disabled", Enabled = false }] };
        var results = await IntegrationSmokeRunner.RunAsync(config, new Secrets(), http, dir.Path);
        Assert.Equal("pass", results.Single(r => r.Provider == id).Status);
        Assert.Equal("skip", results.Single(r => r.Provider == "disabled").Status);
        Assert.Equal("skip", results.Single(r => r.Provider == "meshy").Status);
        Assert.True(File.Exists(Path.Combine(dir.Path, "smoketest.json")));
    }
}
