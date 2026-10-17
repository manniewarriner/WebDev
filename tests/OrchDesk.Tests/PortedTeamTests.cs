using OrchDesk.Core.Agent;
using OrchDesk.Core.Orchestration;
using OrchDesk.Core.Permissions;
using OrchDesk.Core.Tools;

namespace OrchDesk.Tests;

public sealed partial class ManagedTeamTests
{
    [Fact]
    public async Task BaselineArchitectSpecAndOpusReviewReachTheBranchWorker()
    {
        using var dir = new TempDir(); bool sawSpec = false, reviewed = false;
        var worker = new Fake("qwen", r => { sawSpec |= r.Messages.Last().Content!.Contains("ARCHITECT SPEC:"); return "implemented"; });
        var pm = new Fake("opus", r => {
            var prompt = r.Messages.Last().Content!;
            Assert.True(r.ReadOnly);
            if (prompt.StartsWith("Split")) return """[{"id":"a","title":"Architecture","skill":"code","size":"large","deps":[],"files":[]}]""";
            if (prompt.StartsWith("Write a precise edit")) return "Add a Widget class";
            if (prompt.StartsWith("Review")) { reviewed = true; return "PASS"; }
            return "Summary";
        });
        var runner = new TeamOrchestrator(() => new AgentLoop(ToolRegistry.CreateDefault(), new PermissionGate(new PermissionPolicy(), null)), new(), new() { ArchitectSplit = true, OpusReviewsEveryAttempt = true });
        var result = await runner.RunAsync("build", dir.Path, pm, [new(worker, ["*"], ["*"])], false, "", default, verify: false);
        Assert.Equal("done", result.Status); Assert.True(sawSpec); Assert.True(reviewed);
    }

    [Fact]
    public async Task BaselineModelCallCapStopsBeforeTheWorkerTransportStarts()
    {
        using var dir = new TempDir();
        var worker = new Fake("qwen", _ => "done");
        var pm = new Fake("opus", _ => """[{"id":"a","title":"A","skill":"code","size":"small","deps":[],"files":[]}]""");
        var runner = new TeamOrchestrator(() => new AgentLoop(ToolRegistry.CreateDefault(), new PermissionGate(new PermissionPolicy(), null)), new(), new() { MaxModelCalls = 1 });
        await Assert.ThrowsAsync<InvalidOperationException>(() => runner.RunAsync("build", dir.Path, pm, [new(worker, ["*"], ["*"])], false, "", default, verify: false));
        Assert.Equal(0, worker.Calls); Assert.Equal(1, pm.Calls);
    }
}
