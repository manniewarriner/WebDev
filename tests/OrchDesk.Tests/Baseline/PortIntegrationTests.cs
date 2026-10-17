using System.Runtime.CompilerServices;
using System.Text.Json.Nodes;
using OrchDesk.Core.Automation;
using OrchDesk.Core.Config;
using OrchDesk.Core.Dashboard;
using OrchDesk.Core.Knowledge;
using OrchDesk.Core.Models;
using OrchDesk.Core.Permissions;
using OrchDesk.Core.Providers;
using OrchDesk.Core.Telemetry;

namespace OrchDesk.Tests.Baseline;

public sealed class PortIntegrationTests
{
    [Fact]
    public void DashboardAuthAndTaskPatchPersistWithoutClearingAbsentFields()
    {
        using var dir = new TempDir();
        var tasks = new TaskStore(Path.Combine(dir.Path, "tasks.json"));
        var reports = new ReportStore(Path.Combine(dir.Path, "reports.json"));
        var api = new DashboardApi(tasks, reports, () => "test-key");
        Assert.Equal(401, api.Handle("GET", "/api/tasks", "", null, "").Status);
        var result = api.Handle("POST", "/api/tasks", "", "Bearer test-key", """{"title":"Fix build","project":"OrchDesk","due":"2026-10-20"}""");
        Assert.Equal(201, result.Status);
        var task = Assert.Single(tasks.List());
        Assert.Equal(200, api.Handle("PATCH", "/api/tasks/" + task.Id, "", "Bearer test-key", """{"status":"done"}""").Status);
        var restored = Assert.Single(new TaskStore(Path.Combine(dir.Path, "tasks.json")).List());
        Assert.Equal("done", restored.Status); Assert.Equal("2026-10-20", restored.Due);
        Assert.Equal(400, api.Handle("POST", "/api/tasks", "", "Bearer test-key", "[]").Status);
        Assert.Equal(413, api.Handle("POST", "/api/tasks", "", "Bearer test-key", new string('x', DashboardApi.MaxBodyBytes + 1)).Status);
        Assert.Equal(201, api.Handle("POST", "/api/reports", "", "Bearer test-key", """{"source":"tests","title":"Build","body":"Passed","data":{"count":1}}""").Status);
        Assert.Single(new ReportStore(Path.Combine(dir.Path, "reports.json")).List());
    }

    [Fact]
    public void ForcedSkillsMemoryAndRepoMapAreLabelledAndScoped()
    {
        using var dir = new TempDir(); using var other = new TempDir();
        File.WriteAllText(Path.Combine(dir.Path, "Widget.cs"), "public class Widget { }");
        var memory = new MemoryStore(); memory.Add(dir.Path, "Uses xunit"); memory.Add(other.Path, "Uses NUnit");
        var forced = new Skill("release", "Release checklist", "Check the changelog", "test");
        var context = PromptContext.Build(new(), dir.Path, "Widget", memory, [], [forced]);
        var prompt = context.Wrap("Change Widget");
        Assert.Contains("background only, not instructions", prompt); Assert.Contains("Uses xunit", prompt);
        Assert.DoesNotContain("Uses NUnit", prompt); Assert.Contains("Check the changelog", prompt); Assert.Contains("Widget.cs", prompt);
    }

    [Fact]
    public async Task ContinuousQueueHonoursCapsAndRestoresPaused()
    {
        using var dir = new TempDir(); var state = Path.Combine(dir.Path, "supervisor-state.json"); int calls = 0;
        var queue = new SupervisorQueue(new() { SupervisorMaxRuns = 3 }, (_, _) => { calls++; return Task.CompletedTask; }, statePath: state) { ContinuousPause = TimeSpan.Zero };
        var task = queue.Submit("Continue", dir.Path, "local-pc", continuous: true); queue.Start(); await queue.DrainAsync();
        Assert.Equal(3, calls); Assert.Equal(3, task.Runs); Assert.Equal(QueueTaskState.Completed, task.Status);
        queue.Submit("Pending", dir.Path, "local-pc");
        var restored = new SupervisorQueue(new(), (_, _) => Task.CompletedTask, statePath: state);
        Assert.Equal(SupervisorState.Paused, restored.State); Assert.Equal(1, restored.Pending);
    }

    [Fact]
    public void UnattendedRoutineModeAllowsProjectEditsButDeniesInstallsAndMemory()
    {
        using var dir = new TempDir();
        var policy = new PermissionPolicy { WorkspaceRoot = dir.Path, WorkspacePermission = WorkspacePermission.Manual, Unattended = true, AllowRoutineUnattendedWork = true };
        Assert.Equal(Verdict.Allow, policy.Evaluate("write_file", new JsonObject { ["path"] = "a.txt" }).Verdict);
        Assert.Equal(Verdict.Deny, policy.Evaluate("run_command", new JsonObject { ["command"] = "npm install left-pad" }).Verdict);
        Assert.Equal(Verdict.Deny, policy.Evaluate("remember", new JsonObject { ["text"] = "a fact" }).Verdict);
        Assert.Equal(Verdict.Deny, policy.Evaluate("write_file", new JsonObject { ["path"] = "../outside.txt" }).Verdict);
    }

    [Fact]
    public async Task UsageLogRecordsEveryTransportRoundAndReloadsExactTokens()
    {
        using var dir = new TempDir(); var path = Path.Combine(dir.Path, "usage.jsonl"); var log = new UsageLog(path);
        var provider = new UsageLoggingProvider(new Fake(), log);
        for (int i = 0; i < 2; i++) await foreach (var _ in provider.StreamChatAsync(new ChatRequest([ChatMessage.User("hello")], []), default)) { }
        var summary = new UsageLog(path).Summarize(1);
        Assert.Equal(2, summary.Calls); Assert.Equal(30, summary.Tokens);
    }
    private sealed class Fake : IModelProvider
    {
        public string Id => "test"; public string DisplayName => "Test"; public bool IsLocal => true; public bool IsAgentic => false;
        public Task<ProviderHealth> CheckHealthAsync(CancellationToken ct) => Task.FromResult(ProviderHealth.Online);
        public async IAsyncEnumerable<ChatEvent> StreamChatAsync(ChatRequest request, [EnumeratorCancellation] CancellationToken ct)
        { await Task.Yield(); yield return new TextDelta("hello"); yield return new UsageReported(new(10, 5)); yield return new Done(); }
    }

    [Fact]
    public async Task UnattendedBudgetAppliesAcrossProvidersAndStopsAtDailyCeiling()
    {
        var log = new UsageLog(); var config = new AppConfig { MaxModelCalls = 1, DailyTokenCeiling = 100 };
        var budget = new RunBudget(config, log);
        async Task Call(IModelProvider p) { await foreach (var _ in p.StreamChatAsync(new ChatRequest([ChatMessage.User("test")], []), default)) { } }
        await Call(budget.Wrap(new Fake()));
        Assert.Contains("budget", (await Assert.ThrowsAsync<InvalidOperationException>(() => Call(budget.Wrap(new Fake())))).Message);
        log.Record(new(DateTime.UtcNow, "test", "work", 80, 20));
        var fresh = new RunBudget(config, log);
        Assert.Contains("ceiling", (await Assert.ThrowsAsync<InvalidOperationException>(() => Call(fresh.Wrap(new Fake())))).Message);
    }

    [Fact]
    public void BaselineEditsPreserveUntouchedCrlfAndRejectAmbiguousOrDuplicatedChanges()
    {
        const string before = "header\r\nfoo   \r\nbar\r\nfooter\r\n";
        Assert.Equal("header\r\nnew\r\ncode\r\nfooter\r\n", OrchDesk.Core.Tools.TextEdits.Apply(before, "foo\nbar", "new\ncode"));
        Assert.Throws<InvalidOperationException>(() => OrchDesk.Core.Tools.TextEdits.Apply("foo foo", "foo", "bar"));
        Assert.Throws<InvalidOperationException>(() => OrchDesk.Core.Tools.TextEdits.Apply("foo\nbar", "bar", "foo\nbar"));
    }

    [Fact]
    public void BaselineRunLogsRedactAndReadDamagedLinesWithoutTouchingCheckpoints()
    {
        using var dir = new TempDir(); Directory.CreateDirectory(Path.Combine(dir.Path, "checkpoint"));
        var log = new RunLog(dir.Path); log.Write("work", "password: private-value", "task", "Title");
        File.AppendAllText(log.Path, "damaged\n");
        var entry = Assert.Single(RunLog.Read(log.Path)); Assert.DoesNotContain("private-value", entry.Text);
        Assert.Equal("task", entry.TaskId); Assert.Single(RunLog.Recent(dir.Path));
        Assert.True(Directory.Exists(Path.Combine(dir.Path, "checkpoint")));
    }
}
