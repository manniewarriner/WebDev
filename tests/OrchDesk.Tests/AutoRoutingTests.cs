using System.Runtime.CompilerServices;
using System.Net;
using OrchDesk.Core.Accounts;
using OrchDesk.Core.Agent;
using OrchDesk.Core.Config;
using OrchDesk.Core.Models;
using OrchDesk.Core.Orchestration;
using OrchDesk.Core.Permissions;
using OrchDesk.Core.Providers;
using OrchDesk.Core.Tools;

namespace OrchDesk.Tests;

public class AutoRoutingTests
{
    private static Conversation Create(string workspace, params IModelProvider[] providers) =>
        new(new AgentLoop(new ToolRegistry(), new PermissionGate(new PermissionPolicy(), null)), new AccountRotator(providers), workspace);

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Second_turn_preserves_history_and_sticks_until_failure(bool fails)
    {
        using var dir = new TempDir();
        var a = new FakeProvider("a", _ => FakeProvider.Say("OK"), _ => fails ? [new ProviderUnavailable("a", "offline")] : FakeProvider.Say("kiwi"));
        var b = new FakeProvider("b", _ => FakeProvider.Say("kiwi"));
        var convo = Create(dir.Path, a, b);
        await convo.SendAsync("Remember kiwi", default).ToListAsync();
        await convo.SendAsync("What word?", default).ToListAsync();
        Assert.Equal(fails ? "b" : "a", convo.LastProvider!.Id);
        if (fails) {
            Assert.Contains(b.Requests.Single().Messages, m => m.Content == "Remember kiwi");
            Assert.Contains(b.Requests.Single().Messages, m => m.Content == "OK");
        } else Assert.Empty(b.Requests);
    }

    [Fact]
    public async Task Successful_fallback_stays_selected_even_if_earlier_account_recovers()
    {
        using var dir = new TempDir();
        var a = new FakeProvider("a", _ => [new ProviderUnavailable("a", "offline")]);
        var b = new FakeProvider("b", _ => FakeProvider.Say("OK"));
        var rotator = new AccountRotator([a,b]) { OfflineRetryAfter = TimeSpan.Zero };
        var convo = new Conversation(new AgentLoop(new ToolRegistry(), new PermissionGate(new PermissionPolicy(), null)), rotator, dir.Path);
        await convo.SendAsync("one", default).ToListAsync();
        await convo.SendAsync("two", default).ToListAsync();
        Assert.Single(a.Requests);
        Assert.Equal(2, b.Requests.Count);
    }

    [Fact]
    public async Task Unavailable_summary_includes_failed_and_skipped_accounts_and_remembers_offline()
    {
        using var dir = new TempDir();
        var a = new FakeProvider("pc", _ => [new ProviderUnavailable("pc", "offline")]);
        var b = new FakeProvider("cloud", _ => [new UsageLimitHit("cloud", "quota")]);
        var convo = Create(dir.Path, a,b);
        var events = await convo.SendAsync("one", default).ToListAsync();
        Assert.Contains("pc: offline", events.OfType<ErrorEvent>().Single().Message);
        Assert.Contains("cloud: limit", events.OfType<ErrorEvent>().Single().Message);
        await convo.SendAsync("two", default).ToListAsync();
        Assert.Single(a.Requests); Assert.Single(b.Requests);
    }

    [Fact]
    public async Task Empty_response_and_provider_error_are_diagnostic_failures()
    {
        using var dir = new TempDir();
        var convo = Create(dir.Path, new FakeProvider("empty", _ => []), new FakeProvider("auth", _ => [new ErrorEvent("401 login required")]));
        var events = await convo.SendAsync("hello", default).ToListAsync();
        var error = events.OfType<ErrorEvent>().Single().Message;
        Assert.Contains("empty: offline", error);
        Assert.Contains("auth: not logged in", error);
    }

    [Theory]
    [InlineData(true, "mimo-v2.6-flash-free")]
    [InlineData(false, "configured")]
    public void Free_preference_uses_only_synced_models(bool prefer, string expected)
    {
        var config = new AppConfig { PreferFreeModels = prefer };
        var provider = new ProviderConfig { Id = "opencode-go", Model = "configured", AvailableModels = ["mimo-v2.6-flash-free", "longcat-2.5-preview-free"] };
        Assert.Equal(expected, ProviderFactory.AutoModel(config, provider));
        provider.AvailableModels.Clear();
        Assert.Equal("configured", ProviderFactory.AutoModel(config, provider));
    }

    [Fact]
    public void Repeated_send_parses_in_order() => Assert.Equal(new[] { "a", "b" }, SendOptions.Parse(["--send", "a", "--send", "b"])!.Texts);

    private sealed class BlackHole : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) {
            await Task.Delay(Timeout.Infinite, ct); return new(HttpStatusCode.OK);
        }
    }

    [Fact]
    public async Task Blackholed_headers_fall_back_and_next_turn_skips_pc()
    {
        using var dir = new TempDir();
        using var http = new HttpClient(new BlackHole());
        var pc = new OpenAiCompatProvider("remote-pc", "Desktop PC", new("http://fake/v1", "qwen", HeadersTimeout: TimeSpan.FromMilliseconds(30)), false, http);
        var cloud = new FakeProvider("cloud", _ => FakeProvider.Say("OK"));
        var rotator = new AccountRotator([pc, cloud]);
        rotator.Find("remote-pc")!.Health = ProviderHealth.Online;
        var convo = new Conversation(new AgentLoop(new ToolRegistry(), new PermissionGate(new PermissionPolicy(), null)), rotator, dir.Path);
        await convo.SendAsync("one", default).ToListAsync();
        Assert.Equal("cloud", convo.LastProvider!.Id);
        Assert.Contains(convo.Attempts, a => a.ProviderId == "remote-pc" && a.Reason.Contains("offline"));
        await convo.SendAsync("two", default).ToListAsync();
        Assert.Single(convo.Attempts);
    }

    private sealed class SilentProvider : IModelProvider
    {
        public string Id => "silent";
        public string DisplayName => Id;
        public bool IsLocal => true;
        public bool IsAgentic => false;
        public Task<ProviderHealth> CheckHealthAsync(CancellationToken ct) => Task.FromResult(ProviderHealth.Online);
        public async IAsyncEnumerable<ChatEvent> StreamChatAsync(ChatRequest request, [EnumeratorCancellation] CancellationToken ct) {
            await Task.Delay(Timeout.Infinite, ct); yield break;
        }
    }

    [Fact]
    public async Task First_token_timeout_moves_to_cloud()
    {
        using var dir = new TempDir();
        var convo = Create(dir.Path, new SilentProvider(), new FakeProvider("cloud", _ => FakeProvider.Say("OK")));
        convo.FirstTokenTimeout = TimeSpan.FromMilliseconds(30);
        await convo.SendAsync("hello", default).ToListAsync();
        Assert.Equal("cloud", convo.LastProvider!.Id);
    }
}

