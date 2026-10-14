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
    private sealed class Handler : HttpMessageHandler
    {
        private int _turn;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var message = new JsonObject();
            if (request.Method == HttpMethod.Post)
            {
                _turn++;
                if (_turn is 2 or 3)
                {
                    var args = new JsonObject { ["path"] = "smoke.txt" };
                    if (_turn == 2) args["content"] = "ORCHDESK_OK";
                    message["tool_calls"] = new JsonArray(new JsonObject {
                        ["id"] = "tool-" + _turn,
                        ["function"] = new JsonObject { ["name"] = _turn == 2 ? "write_file" : "read_file", ["arguments"] = args.ToJsonString() }
                    });
                }
                else message["content"] = "ORCHDESK_OK";
            }
            var body = new JsonObject { ["choices"] = new JsonArray(new JsonObject { ["message"] = message, ["finish_reason"] = "stop" }) };
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json") });
        }
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
