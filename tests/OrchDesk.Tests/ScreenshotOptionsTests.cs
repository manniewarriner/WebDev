using OrchDesk.Core.Config;
using Xunit;

namespace OrchDesk.Tests;

public class ScreenshotOptionsTests
{
    [Theory]
    [InlineData("--screenshot shot.png", true)]
    [InlineData("--screenshot shot.png --real", false)]
    [InlineData("--screenshot shot.png --config config.json", false)]
    [InlineData("--screenshot shot.png --config config.json --demo", true)]
    [InlineData("--screenshot shot.png --real --demo", true)]
    [InlineData("--SCREENSHOT shot.png --CONFIG config.json", false)]
    [InlineData("--screenshot shot.png --demo", true)]
    [InlineData("", false)]
    public void SelectsFixturesOnlyWhenRequested(string command, bool expected) =>
        Assert.Equal(expected, ScreenshotOptions.UseDemo(command.Split(' ', StringSplitOptions.RemoveEmptyEntries)));
}
