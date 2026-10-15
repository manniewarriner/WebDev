namespace OrchDesk.Core.Config;

public static class ScreenshotOptions
{
    public static bool UseDemo(string[] args) =>
        args.Contains("--screenshot", StringComparer.OrdinalIgnoreCase) &&
        (args.Contains("--demo", StringComparer.OrdinalIgnoreCase) ||
         (!args.Contains("--real", StringComparer.OrdinalIgnoreCase) &&
          !args.Contains("--config", StringComparer.OrdinalIgnoreCase)));
}
