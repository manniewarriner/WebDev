using System.Text.Json.Nodes;
using OrchDesk.Core.Models;
using OrchDesk.Core.Overnight;
using OrchDesk.Core.Permissions;
using OrchDesk.Core.Tools;
using Xunit;
namespace OrchDesk.Tests;
public class OvernightRoundTests
{
    private sealed class Verification(bool green) : ITool {
        public string Name => "run_command"; public string Description => "fake build/test"; public JsonObject Parameters => new();
        public List<string> Commands { get; } = [];
        public Task<string> ExecuteAsync(JsonObject args, ToolContext ctx, CancellationToken ct) { Commands.Add(args["command"]!.GetValue<string>()); return Task.FromResult(green ? "Exit code: 0\npassed" : "Exit code: 1\nfailed"); }
    }
    private static Task<CommandResult> Git(string cwd, params string[] args) => ProcessRunner.RunAsync(ProcessRunner.Exe("git", args, cwd), TimeSpan.FromSeconds(10), default);
    [Theory][InlineData(true)][InlineData(false)]
    public async Task CommitsOnlyGreenRoundToCurrentBranch(bool green)
    {
        using var repo = new TempDir(); using var runs = new TempDir();
        await Git(repo.Path, "init", "-b", "work"); await Git(repo.Path, "config", "user.email", "test@example.com"); await Git(repo.Path, "config", "user.name", "Test");
        repo.File("initial.txt", "initial"); await Git(repo.Path, "add", "-A"); await Git(repo.Path, "commit", "-m", "initial");
        var before = (await Git(repo.Path, "rev-parse", "HEAD")).Output;
        var manager = new FakeProvider("manager", _ => FakeProvider.Say("Write a result file"), _ => FakeProvider.Say("GOAL_COMPLETE"));
        var worker = new FakeProvider("worker", _ => FakeProvider.Call("write_file", "{\"path\":\"result.txt\",\"content\":\"done\"}"), _ => FakeProvider.Say("Done"));
        var verify = new Verification(green);
        var runner = new OvernightRoundRunner(runs.Path, ToolRegistry.CreateDefault(false).Add(verify), new PermissionPolicy());
        var run = new OvernightRun { ManagerId = "manager", WorkerIds = ["worker"], Goal = "Write result", Workspace = repo.Path, MaxRounds = 1, MaxSteps = 4 };
        await runner.RunAsync(run, [manager, worker], default);
        Assert.Equal(green, run.TestsPassed);
        Assert.Equal("work", (await Git(repo.Path, "branch", "--show-current")).Output.Trim());
        Assert.Equal(green, before != (await Git(repo.Path, "rev-parse", "HEAD")).Output);
        Assert.True(File.Exists(Path.Combine(runs.Path, run.Id, "checkpoint.json")));
        Assert.True(File.Exists(Path.Combine(runs.Path, run.Id, "morning-summary.md")));
        Assert.True(File.Exists(Path.Combine(runs.Path, run.Id, "progress.log")));
        Assert.Equal(green ? new[] { "dotnet build", "dotnet test" } : new[] { "dotnet build" }, verify.Commands);
        Assert.Equal("done", File.ReadAllText(Path.Combine(repo.Path, "result.txt")));
    }
    [Theory]
    [InlineData("GOAL_COMPLETE", "Completed")]
    [InlineData("Review finished.\r\n  GOAL_COMPLETE  \r\n", "Completed")]
    [InlineData("GOAL_COMPLETE_PENDING", "Round budget reached")]
    [InlineData("Not GOAL_COMPLETE yet", "Round budget reached")]
    [InlineData("goal_complete", "Round budget reached")]
    public async Task ManagerReviewRequiresStandaloneCompletionToken(string reviewText, string expectedStatus)
    {
        using var repo = new TempDir(); using var runs = new TempDir();
        await Git(repo.Path, "init", "-b", "work"); await Git(repo.Path, "config", "user.email", "test@example.com"); await Git(repo.Path, "config", "user.name", "Test");
        repo.File("initial.txt", "initial"); await Git(repo.Path, "add", "-A"); await Git(repo.Path, "commit", "-m", "initial");
        var manager = new FakeProvider("manager", _ => FakeProvider.Say("Write a result file"), _ => FakeProvider.Say(reviewText));
        var worker = new FakeProvider("worker", _ => FakeProvider.Call("write_file", "{\"path\":\"result.txt\",\"content\":\"done\"}"), _ => FakeProvider.Say("Done"));
        var verify = new Verification(true);
        var runner = new OvernightRoundRunner(runs.Path, ToolRegistry.CreateDefault(false).Add(verify), new PermissionPolicy());
        var run = new OvernightRun { ManagerId = "manager", WorkerIds = ["worker"], Goal = "Write result", Workspace = repo.Path, MaxRounds = 1, MaxSteps = 4 };
        await runner.RunAsync(run, [manager, worker], default);
        Assert.Equal(expectedStatus, run.Status);
    }

    [Fact] public void WorkerCannotCommitOrPush()
    {
        var policy = new PermissionPolicy { HostManagedCommits = true };
        Assert.Equal(Verdict.Deny, policy.Evaluate("git_commit", new()).Verdict);
        Assert.Equal(Verdict.Deny, policy.Evaluate("git_push", new()).Verdict);
        Assert.Equal(Verdict.Deny, policy.EvaluateCommand("git commit -am change").Verdict);
        Assert.False(new OvernightRun().ShutdownWhenDone);
    }
}
