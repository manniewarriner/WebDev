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
    [InlineData(true, "configured")]
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

    private sealed class TestSecrets : ISecretProtector
    {
        public string Protect(string value) => "protected:" + value;
        public string Unprotect(string value) => value["protected:".Length..];
    }

    private sealed class ConfiguredChatHandler(string id) : HttpMessageHandler
    {
        public int Calls { get; private set; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Calls++;
            Assert.Equal(HttpMethod.Post, request.Method);
            Assert.Equal("https://configured.test/v1/chat/completions", request.RequestUri!.AbsoluteUri);
            Assert.Equal("test-key", request.Headers.Authorization?.Parameter);
            if (id == "remote-pc")
            {
                Assert.Equal("test-id", request.Headers.GetValues("CF-Access-Client-Id").Single());
                Assert.Equal("test-secret", request.Headers.GetValues("CF-Access-Client-Secret").Single());
            }
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) {
                Content = new StringContent("{\"choices\":[{\"message\":{\"content\":\"pong\"},\"finish_reason\":\"stop\"}]}", System.Text.Encoding.UTF8, "application/json")
            });
        }
    }

    [Theory]
    [InlineData("remote-pc")]
    [InlineData("opencode-go")]
    public async Task Config_override_credentials_and_chat_route_are_used_for_health_and_send(string id)
    {
        using var dir = new TempDir();
        using var store = ConfigStore.FromArgs(["--config", Path.Combine(dir.Path, "copy.json")]);
        var secrets = new TestSecrets();
        store.Save(new AppConfig { Providers = [new() {
            Id = id, Kind = ProviderKind.OpenAiCompatible, BaseUrl = "https://configured.test/v1", Model = "configured",
            ApiKeyProtected = secrets.Protect("test-key"), AccessClientIdProtected = secrets.Protect("test-id"),
            AccessClientSecretProtected = secrets.Protect("test-secret")
        }] });
        var handler = new ConfiguredChatHandler(id);
        using var http = new HttpClient(handler);
        var provider = new ProviderFactory(secrets, http).CreateAll(store.Load()).Single();
        Assert.Equal(ProviderHealth.Online, await provider.CheckHealthAsync(default));
        var convo = Create(dir.Path, provider);
        var events = await convo.SendAsync("ping", default).ToListAsync();
        Assert.Contains(events, e => e is TextDelta { Text: "pong" });
        Assert.Equal(2, handler.Calls); // one explicit diagnostic probe and one real send; no preflight
    }

    [Theory]
    [InlineData(ProviderHealth.NotConfigured)]
    [InlineData(ProviderHealth.Offline)]
    public async Task Diagnostic_health_does_not_gate_real_send(ProviderHealth health)
    {
        using var dir = new TempDir();
        var provider = new FakeProvider("remote-pc", _ => FakeProvider.Say("pong")) { Health = health };
        var rotator = new AccountRotator([provider]);
        await rotator.RefreshHealthAsync(default);
        var convo = new Conversation(new AgentLoop(new ToolRegistry(), new PermissionGate(new PermissionPolicy(), null)), rotator, dir.Path);
        var events = await convo.SendAsync("ping", default).ToListAsync();
        Assert.Contains(events, e => e is TextDelta { Text: "pong" });
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Default_rotation_starts_with_remote_then_claude_then_opencode(bool preferFree)
    {
        var config = AppConfig.CreateDefault();
        config.PreferFreeModels = preferFree;
        var providers = new[] { "opencode-go", "claude-outlook", "remote-pc" }
            .Select(id => (IModelProvider)new FakeProvider(id)).ToList();
        Assert.Equal(new[] { "remote-pc", "claude-outlook", "opencode-go" },
            ProviderFactory.InRotationOrder(config, providers).Select(p => p.Id));
        Assert.Equal(new[] { "remote-pc", "claude-outlook", "opencode-go" },
            ProviderFactory.InRotationOrder(new AppConfig(), providers).Select(p => p.Id));
    }

    private sealed class ReasoningProvider(ChatEvent? activity = null, TimeSpan? delay = null) : IModelProvider
    {
        public string Id => "reasoning";
        public string DisplayName => Id;
        public bool IsLocal => false;
        public bool IsAgentic => false;
        public Task<ProviderHealth> CheckHealthAsync(CancellationToken ct) => Task.FromResult(ProviderHealth.Online);
        public async IAsyncEnumerable<ChatEvent> StreamChatAsync(ChatRequest request, [EnumeratorCancellation] CancellationToken ct)
        {
            yield return activity ?? new ReasoningDelta("thinking");
            await Task.Delay(delay ?? TimeSpan.FromSeconds(25), ct);
            yield return new TextDelta("pong");
            yield return new Done();
        }
    }

    [Fact]
    public async Task Reasoning_for_25_seconds_keeps_stream_alive()
    {
        using var dir = new TempDir();
        var convo = Create(dir.Path, new ReasoningProvider());
        convo.FirstTokenTimeout = TimeSpan.FromMilliseconds(100);
        var events = await convo.SendAsync("ping", default).ToListAsync();
        Assert.Contains(events, e => e is TextDelta { Text: "pong" });
        Assert.Equal("answered", Assert.Single(convo.Attempts).Reason);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Empty_delta_or_keepalive_keeps_stream_alive(bool empty)
    {
        using var dir = new TempDir();
        var convo = Create(dir.Path, new ReasoningProvider(empty ? new TextDelta("") : new StreamActivity(), TimeSpan.FromMilliseconds(200)));
        convo.FirstTokenTimeout = TimeSpan.FromMilliseconds(50);
        Assert.Contains(await convo.SendAsync("ping", default).ToListAsync(), e => e is TextDelta { Text: "pong" });
    }

    [Fact]
    public async Task AutoNeverAttemptsFreeModelsEvenWithLegacyPreference()
    {
        using var dir = new TempDir();
        var handler = new FreeFallbackHandler();
        using var http = new HttpClient(handler);
        var config = new AppConfig { PreferFreeModels = true, Providers = [new() {
            Id = "opencode-go", Kind = ProviderKind.OpenAiCompatible, BaseUrl = "https://configured.test/v1",
            Model = "configured", AvailableModels = ["mimo-v2.6-flash-free"], ApiKeyProtected = "protected:test-key"
        }] };
        var provider = new ProviderFactory(new TestSecrets(), http).CreateAll(config).Single();
        var convo = Create(dir.Path, provider);
        var events = await convo.SendAsync("ping", default).ToListAsync();
        Assert.Contains(events, e => e is TextDelta { Text: "pong" });
        Assert.Equal(new[] { "configured" }, handler.Models);
        Assert.Equal("configured", provider.ModelId);
        Assert.Equal("answered", convo.Attempts[0].Reason);
    }

    private sealed class FreeFallbackHandler : HttpMessageHandler
    {
        public List<string> Models { get; } = [];
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Assert.Equal("test-key", request.Headers.Authorization?.Parameter);
            var json = System.Text.Json.Nodes.JsonNode.Parse(await request.Content!.ReadAsStringAsync(ct));
            Models.Add(json!["model"]!.GetValue<string>());
            return new(HttpStatusCode.OK) {
                Content = new StringContent("{\"choices\":[{\"message\":{\"content\":\"pong\"}}]}", System.Text.Encoding.UTF8, "application/json")
            };
        }
    }

    [Fact]
    public void Diagnostics_flag_is_opt_in()
    {
        Assert.True(SendOptions.Parse(["--send", "ping", "--diag"])!.Diag);
        Assert.False(SendOptions.Parse(["--send", "ping"])!.Diag);
    }

    [Fact]
    public async Task Attempt_diagnostics_exclude_exception_messages_and_response_bodies()
    {
        using var dir = new TempDir();
        var convo = Create(dir.Path, new FakeProvider("bad", _ => [new ErrorEvent("401 secret-token response-body")]),
            new FakeProvider("good", _ => FakeProvider.Say("pong")));
        await convo.SendAsync("ping", default).ToListAsync();
        var json = System.Text.Json.JsonSerializer.Serialize(convo.Attempts);
        Assert.DoesNotContain("secret-token", json);
        Assert.DoesNotContain("response-body", json);
        Assert.Equal("not logged in", convo.Attempts[0].Reason);
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
        Assert.Equal("answered", convo.Attempts[1].Reason);
        Assert.NotNull(convo.Attempts[0].ExceptionType);
    }
}

