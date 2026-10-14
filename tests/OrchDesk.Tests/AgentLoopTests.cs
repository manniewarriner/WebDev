using OrchDesk.Core.Accounts;
using OrchDesk.Core.Agent;
using OrchDesk.Core.Models;
using OrchDesk.Core.Orchestration;
using OrchDesk.Core.Permissions;
using OrchDesk.Core.Providers;
using OrchDesk.Core.Tools;

namespace OrchDesk.Tests;

public class AgentLoopTests
{
    private static AgentLoop Loop(Func<ApprovalRequest, CancellationToken, Task<ApprovalAnswer>>? approver = null, int maxSteps = 40) =>
        new(ToolRegistry.CreateDefault(), new PermissionGate(new PermissionPolicy(), approver), new AgentLoopOptions { MaxSteps = maxSteps });

    [Fact]
    public async Task Create_edit_and_test_hello_txt()
    {
        using var dir = new TempDir();
        var testCmd = OperatingSystem.IsWindows() ? "findstr OrchDesk hello.txt" : "grep OrchDesk hello.txt";
        var provider = new FakeProvider("fake",
            _ => FakeProvider.Call("write_file", """{"path":"hello.txt","content":"Hello World"}"""),
            _ => FakeProvider.Call("edit_file", """{"path":"hello.txt","old_string":"World","new_string":"OrchDesk"}""", "c2"),
            _ => FakeProvider.Call("run_command", $$"""{"command":"{{testCmd}}"}""", "c3"),
            _ => FakeProvider.Say("Done: hello.txt says Hello OrchDesk."));

        var messages = new List<ChatMessage> { ChatMessage.User("create hello.txt, edit it, run a test") };
        // grep/findstr aren't on the build/test allowlist, so the approver is consulted; approve it.
        var events = await Loop((_, _) => Task.FromResult(ApprovalAnswer.Approve)).RunAsync(provider, messages, new ToolContext(dir.Path), default).ToListAsync();

        Assert.Equal("Hello OrchDesk", File.ReadAllText(Path.Combine(dir.Path, "hello.txt")));
        var results = events.OfType<ToolCallResult>().ToList();
        Assert.Equal(3, results.Count);
        Assert.Contains("Exit code: 0", results[2].Output);
        Assert.IsType<Done>(events[^1]);
        // system + user + 3×(assistant call + tool result) + final assistant
        Assert.Equal(9, messages.Count);
        Assert.Equal(ChatRole.Tool, provider.Requests[3].Messages[^1].Role);
    }

    [Fact]
    public async Task Stops_at_step_limit()
    {
        using var dir = new TempDir();
        var n = 0;
        var provider = new FakeProvider("fake", _ => FakeProvider.Call("list_files", $$"""{"path":".","n":{{n++}}}"""));
        var events = await Loop(maxSteps: 3).RunAsync(provider, [ChatMessage.User("loop")], new ToolContext(dir.Path), default).ToListAsync();
        Assert.Contains("Stopped after 3 steps", events.OfType<ErrorEvent>().Single().Message);
    }

    [Fact]
    public async Task Detects_identical_repeated_calls()
    {
        using var dir = new TempDir();
        var provider = new FakeProvider("fake", _ => FakeProvider.Call("list_files", "{}"));
        var events = await Loop(maxSteps: 4).RunAsync(provider, [ChatMessage.User("x")], new ToolContext(dir.Path), default).ToListAsync();
        Assert.Contains(events.OfType<ToolCallResult>(), r => r.IsError && r.Output.Contains("exact call 3 times"));
    }

    [Fact]
    public async Task Denied_tool_is_reported_to_model_not_executed()
    {
        using var dir = new TempDir();
        dir.File("keep.txt", "x");
        var provider = new FakeProvider("fake",
            _ => FakeProvider.Call("delete_file", """{"path":"keep.txt"}"""),
            _ => FakeProvider.Say("ok"));
        var events = await Loop((_, _) => Task.FromResult(ApprovalAnswer.Deny)).RunAsync(provider, [ChatMessage.User("x")], new ToolContext(dir.Path), default).ToListAsync();
        Assert.True(File.Exists(Path.Combine(dir.Path, "keep.txt")));
        Assert.Contains("denied", events.OfType<ToolCallResult>().Single().Output);
    }

    [Fact]
    public async Task Cancellation_stops_the_loop()
    {
        using var dir = new TempDir();
        using var cts = new CancellationTokenSource();
        var provider = new FakeProvider("fake", _ => { cts.Cancel(); return FakeProvider.Say("never"); });
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            Loop().RunAsync(provider, [ChatMessage.User("x")], new ToolContext(dir.Path), cts.Token).ToListAsync());
    }

    [Fact]
    public async Task Bad_json_arguments_are_returned_as_error()
    {
        using var dir = new TempDir();
        var provider = new FakeProvider("fake", _ => FakeProvider.Call("read_file", "{not json"), _ => FakeProvider.Say("ok"));
        var events = await Loop().RunAsync(provider, [ChatMessage.User("x")], new ToolContext(dir.Path), default).ToListAsync();
        Assert.Contains("not valid JSON", events.OfType<ToolCallResult>().Single().Output);
    }
}

public class FailoverTests
{
    private static AgentLoop Loop() => new(ToolRegistry.CreateDefault(), new PermissionGate(new PermissionPolicy(), null));

    [Fact]
    public async Task Offline_pc_falls_back_to_cloud()
    {
        using var dir = new TempDir();
        var pc = new FakeProvider("pc", _ => [new ProviderUnavailable("pc", "connection refused")]) { IsLocal = true };
        var cloud = new FakeProvider("cloud", _ => FakeProvider.Say("hi from cloud"));
        var rotator = new AccountRotator([pc, cloud]);
        var convo = new Conversation(Loop(), rotator, dir.Path);

        var events = await convo.SendAsync("hello", default).ToListAsync();

        Assert.Contains(events, e => e is StatusUpdate s && s.Text.Contains("offline"));
        Assert.Equal("hi from cloud", string.Concat(events.OfType<TextDelta>().Select(t => t.Text)));
        Assert.Equal(ProviderHealth.Offline, rotator.Find("pc")!.Health);
        Assert.Equal("cloud", rotator.Current()!.Id); // pc is skipped until its retry window passes
        // The failed attempt was rolled back: system, user, assistant only.
        Assert.Equal([ChatRole.System, ChatRole.User, ChatRole.Assistant], convo.Messages.Select(m => m.Role));
    }

    [Fact]
    public async Task Usage_limit_rotates_and_cools_down()
    {
        using var dir = new TempDir();
        var outlook = new FakeProvider("claude-outlook", _ => [new UsageLimitHit("claude-outlook", "usage limit reached")]) { IsAgentic = true };
        var gmail = new FakeProvider("claude-gmail", _ => FakeProvider.Say("gmail here")) { IsAgentic = true };
        var rotator = new AccountRotator([outlook, gmail]);
        var events = await new Conversation(Loop(), rotator, dir.Path).SendAsync("hi", default).ToListAsync();

        Assert.Contains(events, e => e is StatusUpdate s && s.Text.Contains("usage limit"));
        Assert.NotNull(rotator.Find("claude-outlook")!.LastLimitHit);
        Assert.Equal("claude-gmail", rotator.Current()!.Id);
    }

    [Fact]
    public async Task All_unavailable_reports_error()
    {
        using var dir = new TempDir();
        var a = new FakeProvider("a", _ => [new ProviderUnavailable("a", "down")]);
        var events = await new Conversation(Loop(), new AccountRotator([a]), dir.Path).SendAsync("hi", default).ToListAsync();
        Assert.IsType<ErrorEvent>(events[^1]);
    }

    [Fact]
    public async Task Health_refresh_marks_not_configured_unusable()
    {
        var a = new FakeProvider("a") { Health = ProviderHealth.NotConfigured };
        var b = new FakeProvider("b");
        var rotator = new AccountRotator([a, b]);
        await rotator.RefreshHealthAsync(default);
        Assert.Equal("b", rotator.Current()!.Id);
    }
}
