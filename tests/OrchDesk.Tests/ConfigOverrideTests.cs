using OrchDesk.Core.Config;
using Xunit;
namespace OrchDesk.Tests;
public sealed class ConfigOverrideTests
{
    [Fact]
    public void OverrideUsesExactFileAndParentForOutputsAndRoundTripsProtectedValues()
    {
        var directory = Path.Combine(Path.GetTempPath(), "orch-config-" + Guid.NewGuid().ToString("N"));
        try
        {
            using var store = ConfigStore.FromArgs(["--smoketest", "--config", Path.Combine(directory, "custom.json")]);
            var config = new AppConfig { DisplayName = "Override", Providers = [new() { ApiKeyProtected = "already-protected" }] };
            store.Save(config);
            Assert.Equal(directory, store.Directory);
            Assert.Equal(Path.Combine(directory, "custom.json"), store.FilePath);
            Assert.False(File.Exists(Path.Combine(directory, "config.json")));
            Assert.Equal("Override", store.Load().DisplayName);
            Assert.Equal("already-protected", store.Load().Providers[0].ApiKeyProtected);
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }
    [Fact]
    public void MissingConfigPathIsRejected()
    {
        Assert.Throws<ArgumentException>(() => ConfigStore.FromArgs(["--config"]));
        Assert.Throws<ArgumentException>(() => ConfigStore.FromArgs(["--config", "--smoketest"]));
    }
}
