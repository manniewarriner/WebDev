using OrchDesk.Core.Providers;
using Xunit;
namespace OrchDesk.Tests;
public class CliShimTests
{
    [Fact]
    public void ClaudeEnvironmentExplicitlyRemovesApiCredentials()
    {
        var environment = new ClaudeCliProvider("claude", "Claude", new("subscription-profile")).BuildEnvironment();
        foreach (var name in new[] { "ANTHROPIC_API_KEY", "ANTHROPIC_AUTH_TOKEN" })
        {
            Assert.True(environment.ContainsKey(name));
            Assert.Null(environment[name]);
        }
        var child = CliRunner.CreateStartInfo("claude.exe", [], null, environment);
        Assert.False(child.Environment.ContainsKey("ANTHROPIC_API_KEY"));
        Assert.False(child.Environment.ContainsKey("ANTHROPIC_AUTH_TOKEN"));
        Assert.Equal("subscription-profile", child.Environment["CLAUDE_CONFIG_DIR"]);
    }

    [Fact] public async Task WindowsBatchVersionTestUsesSafeArguments()
    {
        if (!OperatingSystem.IsWindows()) return;
        using var dir = new TempDir(); var path = dir.File("fake cli.cmd", "@echo off\r\nif \"%~1\"==\"--version\" echo 1.0\r\n");
        var lines = await CliRunner.StreamAsync(CliRunner.CreateStartInfo(path, ["--version"], dir.Path), null, default).ToListAsync();
        Assert.Contains(lines, l => !l.IsStdErr && l.Text == "1.0");
    }
    [Theory][InlineData("x & del y")][InlineData("%SECRET%")][InlineData("bad\"value")]
    public void WindowsBatchOptionsRejectShellExpansion(string option)
    {
        if (!OperatingSystem.IsWindows()) return;
        Assert.Throws<ArgumentException>(() => CliRunner.CreateStartInfo("fake.cmd", [option], null));
    }
}
