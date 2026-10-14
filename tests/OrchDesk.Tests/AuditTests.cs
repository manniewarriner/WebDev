using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using OrchDesk.Core.Agent;
using OrchDesk.Core.Models;
using OrchDesk.Core.Permissions;
using OrchDesk.Core.Providers;
using OrchDesk.Core.Orchestration;
using OrchDesk.Core.Tools;
using OrchDesk.Core.Config;
using OrchDesk.Core.Verification;

namespace OrchDesk.Tests;

public class AuditTests
{
    [Theory]
    [InlineData("dotnet test\necho hidden")]
    [InlineData("dotnet add package Example")]
    [InlineData("dotnet test && git push")]
    [InlineData("dotnet test&echo hidden")]
    [InlineData("dotnet test < input.txt")]
    [InlineData("dotnet test \"C:\\outside\\tests.csproj\"")]
    [InlineData("dotnet test \"..\\tests.csproj\"")]
    public void Risky_commands_ask(string command) => Assert.Equal(Verdict.Ask, new PermissionPolicy().EvaluateCommand(command).Verdict);

    [Fact]
    public void Outside_file_path_asks() => Assert.Equal(Verdict.Ask, new PermissionPolicy().Evaluate("write_file", new JsonObject { ["path"] = "../escape" }).Verdict);

    [Fact]
    public async Task Readonly_planner_cannot_invoke_write_tool()
    {
        using var dir = new TempDir();
        var provider = new FakeProvider("planner", _ => FakeProvider.Call("write_file", """{"path":"bad.txt","content":"bad"}"""), _ => FakeProvider.Say("plan"));
        var loop = new AgentLoop(ToolRegistry.CreateDefault(), new PermissionGate(new PermissionPolicy(), null));
        var events = await loop.RunAsync(provider, [ChatMessage.User("plan")], new ToolContext(dir.Path), default, readOnly: true).ToListAsync();
        Assert.True(events.OfType<ToolCallResult>().Single().IsError);
        Assert.False(File.Exists(Path.Combine(dir.Path, "bad.txt")));
    }

    [Fact]
    public void Rotation_keeps_enabled_providers_omitted_from_order()
    {
        var a = new FakeProvider("a"); var b = new FakeProvider("b");
        Assert.Equal(new[] { "a", "b" }, ProviderFactory.InRotationOrder(new AppConfig { RotationOrder = ["a", "a", "missing"] }, [a, b]).Select(p => p.Id));
    }

    [Fact]
    public async Task Supervisor_test_command_uses_approval_gate()
    {
        using var dir = new TempDir();
        var asked = false;
        var loop = new AgentLoop(ToolRegistry.CreateDefault(), new PermissionGate(new PermissionPolicy(), (_, _) => { asked = true; return Task.FromResult(ApprovalAnswer.Deny); }));
        var supervisor = new Supervisor(loop) { MaxRounds = 1 };
        var events = await supervisor.RunAsync("task", new FakeProvider("plan", _ => FakeProvider.Say("plan")), new FakeProvider("work", _ => FakeProvider.Say("done")), "echo unsafe", dir.Path, default).ToListAsync();
        Assert.True(asked);
        Assert.Null(supervisor.Result);
        Assert.IsType<ErrorEvent>(events.Last());
    }

    [Fact]
    public async Task Cost_router_test_command_cannot_bypass_approval()
    {
        using var dir = new TempDir();
        var loop = new AgentLoop(ToolRegistry.CreateDefault(), new PermissionGate(new PermissionPolicy(), null));
        var router = new CostRouter(loop);
        var events = await router.RunAsync("task", [new FakeProvider("work", _ => FakeProvider.Say("done"))], "echo denied", dir.Path, default).ToListAsync();
        Assert.IsType<ErrorEvent>(events.Last());
        Assert.DoesNotContain(events, e => e is ToolCallResult { Name: "tests" });
    }

    [Theory]
    [InlineData("127.0.0.1", true)]
    [InlineData("::1", true)]
    [InlineData("fc00::1", true)]
    [InlineData("fe80::1", true)]
    [InlineData("::ffff:192.168.1.1", true)]
    [InlineData("100.64.0.1", true)]
    [InlineData("10.0.0.1", true)]
    [InlineData("8.8.8.8", false)]
    [InlineData("2606:4700:4700::1111", false)]
    public void Web_fetch_classifies_addresses(string address, bool blocked) => Assert.Equal(blocked, WebFetchTool.IsPrivate(IPAddress.Parse(address)));

    [Fact]
    public async Task Selftest_reports_every_check_without_provider_secrets()
    {
        using var dir = new TempDir();
        var provider = new FakeProvider("fake",
            _ => FakeProvider.Say("hello"),
            _ => FakeProvider.Call("write_file", """{"path":"hello.txt","content":"hello"}"""),
            _ => FakeProvider.Call("edit_file", """{"path":"hello.txt","old_string":"hello","new_string":"hello world"}""", "edit"),
            _ => FakeProvider.Say("secret-value-should-not-be-saved"));
        var report = await SelfTestRunner.RunAsync(dir.Path, provider, fetch: (_, _) => Task.FromResult("<untrusted_web_content>Example Domain</untrusted_web_content>"));
        Assert.True(report.Passed);
        Assert.Equal(8, report.Items.Count);
        var json = File.ReadAllText(Path.Combine(dir.Path, "selftest.json"));
        Assert.DoesNotContain("secret-value", json);
    }

    [Theory]
    [InlineData("application/json", """{"choices":[{"message":{"content":"answer","tool_calls":[{"id":"c","function":{"name":"read_file","arguments":"{}"}}]}}]}""")]
    [InlineData("text/event-stream", "data: {\"choices\":[{\"delta\":{\"reasoning_content\":\"think\"}}]}\n\ndata: {\"choices\":[{\"delta\":{\"content\":\"answer\",\"tool_calls\":[{\"index\":0,\"id\":\"c\",\"function\":{\"name\":\"read_file\",\"arguments\":\"{}\"}}]}}]}\n\ndata: [DONE]\n")]
    public async Task Http_provider_handles_both_reply_formats(string mediaType, string body)
    {
        using var http = new HttpClient(new ReplyHandler(mediaType, body));
        var provider = new OpenAiCompatProvider("local", "local", new OpenAiCompatOptions("http://localhost/v1", "qwen"), true, http);
        var events = await provider.StreamChatAsync(new ChatRequest([ChatMessage.User("hi")], []), default).ToListAsync();
        Assert.Equal("answer", events.OfType<TextDelta>().Single().Text);
        Assert.Equal("read_file", events.OfType<ToolCallRequested>().Single().Call.Name);
    }
    private sealed class ReplyHandler(string mediaType, string body) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, mediaType) });
    }
}
