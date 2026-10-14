using System.Net;
using System.Text;
using OrchDesk.Core.Agent;
using OrchDesk.Core.Models;
using OrchDesk.Core.Permissions;
using OrchDesk.Core.Providers;
using OrchDesk.Core.Tools;
using Xunit;
namespace OrchDesk.Tests;
public class RelayTests
{
    private sealed class Handler(Func<HttpRequestMessage, Task<HttpResponseMessage>> reply) : HttpMessageHandler {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage r, CancellationToken ct) => reply(r);
    }
    [Fact] public async Task AccessLoginPageIsOfflineEvenWith200()
    {
        using var http = new HttpClient(new Handler(_ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("login", Encoding.UTF8, "text/html") })));
        var provider = new OpenAiCompatProvider("remote", "Remote", new("https://relay.test/v1", "model"), false, http);
        Assert.Equal(ProviderHealth.Offline, await provider.CheckHealthAsync(default));
        var events = await provider.StreamChatAsync(new([ChatMessage.User("hi")], []), default).ToListAsync();
        Assert.Contains(events, e => e is ProviderUnavailable);
    }
    [Fact] public async Task AccessHeadersSurviveChatToolRoundTrip()
    {
        using var dir = new TempDir(); dir.File("read.txt", "ROUNDTRIP"); var requests = 0;
        using var http = new HttpClient(new Handler(async r => {
            Assert.Equal("id", r.Headers.GetValues("CF-Access-Client-Id").Single());
            Assert.Equal("secret", r.Headers.GetValues("CF-Access-Client-Secret").Single());
            var body = await r.Content!.ReadAsStringAsync(); requests++;
            if (requests == 2) Assert.Contains("ROUNDTRIP", body);
            var json = requests == 1 ? "{\"choices\":[{\"message\":{\"tool_calls\":[{\"id\":\"t1\",\"type\":\"function\",\"function\":{\"name\":\"read_file\",\"arguments\":\"{\\\"path\\\":\\\"read.txt\\\"}\"}}]},\"finish_reason\":\"tool_calls\"}]}" : "{\"choices\":[{\"message\":{\"content\":\"ROUNDTRIP\"},\"finish_reason\":\"stop\"}]}";
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(json, Encoding.UTF8, "application/json") };
        }));
        var provider = new OpenAiCompatProvider("relay", "Relay", new("https://relay.test/v1", "model", ExtraHeaders: new Dictionary<string, string> { ["CF-Access-Client-Id"] = "id", ["CF-Access-Client-Secret"] = "secret" }), false, http);
        var loop = new AgentLoop(ToolRegistry.CreateDefault(), new PermissionGate(new PermissionPolicy(), null));
        var events = await loop.RunAsync(provider, [ChatMessage.User("read")], new(dir.Path), default).ToListAsync();
        Assert.Equal(2, requests); Assert.Contains(events, e => e is ToolCallResult { IsError: false });
        Assert.Contains(events, e => e is TextDelta { Text: "ROUNDTRIP" });
    }
}
