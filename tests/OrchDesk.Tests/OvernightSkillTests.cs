using OrchDesk.Core.Overnight;
using OrchDesk.Core.Permissions;
using OrchDesk.Core.Tools;
using Xunit;

namespace OrchDesk.Tests;
public class OvernightSkillTests
{
    [Fact]
    public async Task CheckpointResumesToolsAndHistoryWithoutRepeatingCompletedEdits()
    {
        using var dir = new TempDir();
        var policy = new PermissionPolicy(); policy.ExtraAllowedCommandPrefixes.Add("echo verified");
        var skill = new OvernightSkill(dir.Path, ToolRegistry.CreateDefault(), policy);
        var run = new OvernightRun { Goal = "Create a file", Workspace = dir.Path, ManagerId = "manager", MaxSteps = 1, TestCommand = "echo verified" };
        await skill.RunAsync(run, new FakeProvider("manager", _ => FakeProvider.Call("write_file", "{\"path\":\"result.txt\",\"content\":\"done\"}")), default);
        var restored = new OvernightSkill(dir.Path, ToolRegistry.CreateDefault(), policy).Load(skill.PathFor(run.Id));
        Assert.Equal("done", File.ReadAllText(Path.Combine(dir.Path, "result.txt")));
        restored.MaxSteps = 2;
        var manager = new FakeProvider("manager", _ => FakeProvider.Say("Finished"));
        await skill.RunAsync(restored, manager, default);
        Assert.Contains(manager.Requests[0].Messages, m => m.ToolName == "write_file");
        Assert.True(restored.TestsPassed);
        Assert.Equal("Completed", restored.Status);
        Assert.True(File.Exists(Path.ChangeExtension(skill.PathFor(run.Id), ".md")));
    }
    [Fact]
    public async Task RiskyActionIsQueuedAndIndependentWorkContinues()
    {
        using var dir = new TempDir(); dir.File("keep.txt", "keep");
        var policy = new PermissionPolicy(); policy.ExtraAllowedCommandPrefixes.Add("echo verified");
        var skill = new OvernightSkill(dir.Path, ToolRegistry.CreateDefault(), policy);
        var run = new OvernightRun { Goal = "Work", Workspace = dir.Path, ManagerId = "manager", MaxSteps = 3, TestCommand = "echo verified" };
        var manager = new FakeProvider("manager", _ => FakeProvider.Call("delete_file", "{\"path\":\"keep.txt\"}"),
            _ => FakeProvider.Call("write_file", "{\"path\":\"safe.txt\",\"content\":\"done\"}", "c2"), _ => FakeProvider.Say("Done"));
        await skill.RunAsync(run, manager, default);
        Assert.True(File.Exists(Path.Combine(dir.Path, "keep.txt")));
        Assert.True(File.Exists(Path.Combine(dir.Path, "safe.txt")));
        Assert.Single(skill.Load(skill.PathFor(run.Id)).Approvals);
        Assert.Equal("Needs morning approval", run.Status);
    }
    [Fact]
    public async Task InterruptedSideEffectIsQueuedWithoutReplay()
    {
        using var dir = new TempDir();
        var policy = new PermissionPolicy(); policy.ExtraAllowedCommandPrefixes.Add("echo verified");
        var skill = new OvernightSkill(dir.Path, ToolRegistry.CreateDefault(), policy);
        var call = new OrchDesk.Core.Models.ToolCall("pending", "write_file", "{\"path\":\"unknown.txt\",\"content\":\"overwrite\"}");
        var run = new OvernightRun { Goal = "Resume", Workspace = dir.Path, ManagerId = "manager", MaxSteps = 1, TestCommand = "echo verified", InFlight = call, PendingTools = [call],
            Messages = [OrchDesk.Core.Models.ChatMessage.User("Resume"), OrchDesk.Core.Models.ChatMessage.Assistant(null, [call])] };
        skill.Save(run);
        var restored = skill.Load(skill.PathFor(run.Id));
        await skill.RunAsync(restored, new FakeProvider("manager", _ => FakeProvider.Say("Done")), default);
        Assert.False(File.Exists(Path.Combine(dir.Path, "unknown.txt")));
        Assert.Single(restored.Approvals);
        Assert.Empty(restored.PendingTools);
    }
    [Fact]
    public async Task ExpiredDeadlineStopsBeforeProviderCall()
    {
        using var dir = new TempDir();
        var skill = new OvernightSkill(dir.Path, ToolRegistry.CreateDefault(), new PermissionPolicy());
        var manager = new FakeProvider("manager", _ => FakeProvider.Say("Unexpected"));
        var run = new OvernightRun { Workspace = dir.Path, ManagerId = "manager", Deadline = DateTimeOffset.UtcNow.AddSeconds(-1) };
        await skill.RunAsync(run, manager, default);
        Assert.Empty(manager.Requests);
        Assert.Equal("Deadline reached", run.Status);
    }

}
