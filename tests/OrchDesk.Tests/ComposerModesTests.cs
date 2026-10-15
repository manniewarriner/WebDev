using System.Text.Json.Nodes;
using OrchDesk.Core.Agent;
using OrchDesk.Core.Config;
using OrchDesk.Core.Models;
using OrchDesk.Core.Permissions;
using OrchDesk.Core.Providers;
using OrchDesk.Core.Tools;

namespace OrchDesk.Tests;

public sealed class ComposerModesTests
{
    [Fact]
    public void Claude_uses_host_tools_for_composer_runs()
    {
        var provider = new ClaudeCliProvider("claude", "Claude", new("profile"));
        var request = new ChatRequest([], [], ReadOnly: true, HostManagedTools: true);
        var args = provider.BuildArgs(request, null).ToList();
        Assert.Equal("plan", args[args.IndexOf("--permission-mode") + 1]);
        Assert.Equal("", args[args.IndexOf("--tools") + 1]);
        Assert.Contains("OrchDesk controls all tools", HostToolProtocol.Instruction(request));
    }

    [Theory]
    [InlineData(WorkspacePermission.Manual)]
    [InlineData(WorkspacePermission.FullAccess)]
    public void Existing_denials_and_outside_commands_stay_blocked(WorkspacePermission permission)
    {
        using var dir = new TempDir();
        var policy = new PermissionPolicy { WorkspaceRoot = dir.Path, WorkspacePermission = permission, HostManagedCommits = true };
        Assert.Equal(Verdict.Deny, policy.Evaluate("run_command", new() { ["command"] = "git reset --hard" }).Verdict);
        Assert.Equal(Verdict.Deny, policy.Evaluate("run_command", new() { ["command"] = "Remove-Item ../outside.txt" }).Verdict);
        Assert.Equal(Verdict.Deny, policy.Evaluate("write_file", new() { ["path"] = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".txt") }).Verdict);
    }

    [Theory]
    [InlineData(WorkspacePermission.FullAccess, "write_file", "a.txt", Verdict.Allow)]
    [InlineData(WorkspacePermission.FullAccess, "edit_file", "a.txt", Verdict.Allow)]
    [InlineData(WorkspacePermission.FullAccess, "delete_file", "a.txt", Verdict.Allow)]
    [InlineData(WorkspacePermission.Manual, "write_file", "a.txt", Verdict.Ask)]
    [InlineData(WorkspacePermission.Manual, "delete_file", "a.txt", Verdict.Ask)]
    [InlineData(WorkspacePermission.Manual, "run_command", "", Verdict.Ask)]
    [InlineData(WorkspacePermission.FullAccess, "run_command", "", Verdict.Allow)]
    [InlineData(WorkspacePermission.FullAccess, "write_file", "../outside", Verdict.Deny)]
    [InlineData(WorkspacePermission.FullAccess, "delete_file", "../outside", Verdict.Deny)]
    [InlineData(WorkspacePermission.Manual, "write_file", "../outside", Verdict.Deny)]
    [InlineData(WorkspacePermission.Manual, "delete_file", "../outside", Verdict.Deny)]
    public void Policy_matrix(WorkspacePermission mode, string tool, string path, Verdict expected)
    {
        using var dir = new TempDir();
        var policy = new PermissionPolicy { WorkspaceRoot = dir.Path, WorkspacePermission = mode };
        Assert.Equal(expected, policy.Evaluate(tool, new() { ["path"] = path, ["command"] = "dotnet test" }).Verdict);
    }

    [Fact]
    public void Permissions_and_mode_round_trip_per_workspace()
    {
        using var dir = new TempDir();
        var a = Path.Combine(dir.Path, "A"); var b = Path.Combine(dir.Path, "B");
        var config = new AppConfig { LastWorkMode = WorkMode.Plan };
        config.SetPermission(a + Path.DirectorySeparatorChar, WorkspacePermission.FullAccess);
        var store = new ConfigStore(Path.Combine(dir.Path, "config")); store.Save(config);
        var loaded = store.Load();
        Assert.Equal(WorkspacePermission.FullAccess, loaded.PermissionFor(a));
        Assert.Equal(WorkspacePermission.Manual, loaded.PermissionFor(b));
        Assert.Equal(WorkMode.Plan, loaded.LastWorkMode);
    }

    [Fact]
    public async Task Accept_all_only_applies_to_this_gate_and_does_not_override_deny()
    {
        using var dir = new TempDir(); var prompts = 0;
        var policy = new PermissionPolicy { WorkspaceRoot = dir.Path, WorkspacePermission = WorkspacePermission.Manual };
        var gate = new PermissionGate(policy, (_, _) => { prompts++; return Task.FromResult(ApprovalAnswer.AlwaysThisSession); });
        Assert.Equal(Verdict.Allow, (await gate.CheckAsync("write_file", new() { ["path"] = "a", ["content"] = "x" }, default)).Verdict);
        Assert.Equal(Verdict.Allow, (await gate.CheckAsync("run_command", new() { ["command"] = "echo hi" }, default)).Verdict);
        Assert.Equal(Verdict.Deny, (await gate.CheckAsync("delete_file", new() { ["path"] = "../outside" }, default)).Verdict);
        Assert.Equal(1, prompts);
        var next = new PermissionGate(policy, (_, _) => Task.FromResult(ApprovalAnswer.Deny));
        Assert.Equal(Verdict.Deny, (await next.CheckAsync("write_file", new() { ["path"] = "a" }, default)).Verdict);
    }

    [Fact]
    public async Task Undo_restores_edits_and_deleted_bytes()
    {
        using var dir = new TempDir(); dir.File("edited.txt", "before"); dir.File("deleted.txt", "keep me");
        var log = new ChangeLog();
        var loop = new AgentLoop(ToolRegistry.CreateDefault(), new(new() { WorkspacePermission = WorkspacePermission.FullAccess }, null)) { Changes = log };
        var ctx = new ToolContext(dir.Path);
        var edit = await loop.ExecuteToolAsync(new("1", "edit_file", """{"path":"edited.txt","old_string":"before","new_string":"after"}"""), ctx, default);
        var delete = await loop.ExecuteToolAsync(new("2", "delete_file", """{"path":"deleted.txt"}"""), ctx, default);
        Assert.False(edit.IsError); Assert.False(delete.IsError);
        Assert.False(File.Exists(Path.Combine(dir.Path, "deleted.txt")));
        foreach (var entry in log.Entries) entry.Undo();
        Assert.Equal("before", File.ReadAllText(Path.Combine(dir.Path, "edited.txt")));
        Assert.Equal("keep me", File.ReadAllText(Path.Combine(dir.Path, "deleted.txt")));
        Assert.All(log.Entries, e => Assert.False(e.CanUndo));
    }

    [Fact]
    public async Task Plan_exposes_read_tools_and_blocks_unadvertised_writes()
    {
        using var dir = new TempDir();
        var provider = new FakeProvider("plan", _ => FakeProvider.Call("write_file", """{"path":"bad","content":"bad"}"""), _ => FakeProvider.Say("1. Inspect files\n2. Implement fix\n3. Run tests"));
        var loop = new AgentLoop(ToolRegistry.CreateDefault(), new(new() { WorkspacePermission = WorkspacePermission.Manual }, null));
        var events = await loop.RunAsync(provider, [ChatMessage.User(WorkPlan.Instruction)], new(dir.Path), default, true).ToListAsync();
        Assert.All(provider.Requests, r => { Assert.True(r.ReadOnly); Assert.DoesNotContain(r.Tools, t => t.Name is "write_file" or "delete_file" or "edit_file" or "run_command" or "git_commit"); });
        Assert.False(File.Exists(Path.Combine(dir.Path, "bad")));
        Assert.Equal(3, WorkPlan.Parse(string.Concat(events.OfType<TextDelta>().Select(e => e.Text))).Count);
    }
}
