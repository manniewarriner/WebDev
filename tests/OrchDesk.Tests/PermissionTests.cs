using System.Text.Json.Nodes;
using OrchDesk.Core.Permissions;

namespace OrchDesk.Tests;

public class PermissionTests
{
    private readonly PermissionPolicy _policy = new();

    [Theory]
    [InlineData("dotnet build", Verdict.Allow)]
    [InlineData("dotnet test --filter Foo", Verdict.Allow)]
    [InlineData("npm test", Verdict.Allow)]
    [InlineData("pytest -q", Verdict.Allow)]
    [InlineData("git status", Verdict.Allow)]
    [InlineData("git push origin main", Verdict.Ask)]
    [InlineData("dotnet test && git push", Verdict.Ask)]
    [InlineData("dotnet test; rm -rf /", Verdict.Ask)]
    [InlineData("npm install left-pad", Verdict.Ask)]
    [InlineData("pip install requests", Verdict.Ask)]
    [InlineData("dotnet add package Newtonsoft.Json", Verdict.Ask)]
    [InlineData("curl https://example.com", Verdict.Ask)]
    [InlineData("del /q *.cs", Verdict.Ask)]
    [InlineData("git reset --hard HEAD~3", Verdict.Ask)]
    [InlineData("type ..\\..\\secrets.txt", Verdict.Ask)]
    [InlineData("powershell -c whoami", Verdict.Ask)]
    [InlineData("dotnet test > out.txt", Verdict.Ask)]
    public void Command_policy(string command, Verdict expected) =>
        Assert.Equal(expected, _policy.EvaluateCommand(command).Verdict);

    [Theory]
    [InlineData("read_file", Verdict.Allow)]
    [InlineData("edit_file", Verdict.Allow)]
    [InlineData("git_commit", Verdict.Allow)]
    [InlineData("delete_file", Verdict.Ask)]
    [InlineData("git_push", Verdict.Ask)]
    [InlineData("mystery_tool", Verdict.Ask)]
    public void Tool_policy(string tool, Verdict expected) =>
        Assert.Equal(expected, _policy.Evaluate(tool, new JsonObject { ["path"] = "a.txt" }).Verdict);

    [Fact]
    public void Unattended_turns_ask_into_deny()
    {
        var p = new PermissionPolicy { Unattended = true };
        Assert.Equal(Verdict.Deny, p.Evaluate("delete_file", new JsonObject { ["path"] = "a" }).Verdict);
        Assert.Equal(Verdict.Allow, p.Evaluate("run_command", new JsonObject { ["command"] = "dotnet test" }).Verdict);
    }

    [Fact]
    public async Task Gate_remembers_always_allow_for_the_session()
    {
        var asked = 0;
        var gate = new PermissionGate(new PermissionPolicy(), (_, _) => { asked++; return Task.FromResult(ApprovalAnswer.AlwaysThisSession); });
        var args = new JsonObject { ["command"] = "npm run format" };
        Assert.Equal(Verdict.Allow, (await gate.CheckAsync("run_command", args, default)).Verdict);
        Assert.Equal(Verdict.Allow, (await gate.CheckAsync("run_command", args, default)).Verdict);
        Assert.Equal(1, asked);
    }

    [Fact]
    public async Task Gate_denies_when_user_denies()
    {
        var gate = new PermissionGate(new PermissionPolicy(), (_, _) => Task.FromResult(ApprovalAnswer.Deny));
        var d = await gate.CheckAsync("delete_file", new JsonObject { ["path"] = "x" }, default);
        Assert.Equal(Verdict.Deny, d.Verdict);
    }
}
