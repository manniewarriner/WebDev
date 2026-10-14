using OrchDesk.Core.Providers;
using Xunit;
namespace OrchDesk.Tests;
public class CliShimTests
{
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
