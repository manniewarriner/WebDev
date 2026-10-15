using System.Net;
using OrchDesk.Core.Config;
using OrchDesk.Core.Integrations;
using Xunit;

namespace OrchDesk.Tests;

public class StartupModelSyncTests
{
    private sealed class Secrets : ISecretProtector
    {
        public string Protect(string s) => s;
        public string Unprotect(string s) => s;
    }
    private sealed class Auth(bool loggedIn) : IClaudeAuthRunner
    {
        public Task<bool> IsLoggedInAsync(ProviderConfig profile, CancellationToken ct) => Task.FromResult(loggedIn);
    }
    private sealed class Handler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.Unauthorized));
    }
    [Theory]
    [InlineData(true, "pass", 3)]
    [InlineData(false, "fail", 0)]
    public async Task SyncChecksClaudeProfileAndRejectsStaleConnectedState(bool loggedIn, string status, int count)
    {
        using var http = new HttpClient(new Handler());
        var profile = new ProviderConfig { Id = "claude", Kind = ProviderKind.ClaudeCli, AvailableModels = ["stale"], ModelsSyncedAt = DateTimeOffset.UtcNow };
        var config = new AppConfig { Providers = [profile, new() { Id = "disabled", Enabled = false }, new() { Id = "cli", Kind = ProviderKind.CodexCli }] };
        var results = await StartupModelSync.RunAsync(config, new(http, new Secrets(), new Auth(loggedIn)));
        Assert.Equal(status, results[0].Status);
        Assert.Equal(count, results[0].ModelCount);
        Assert.Equal(count, profile.AvailableModels.Count);
        Assert.All(results.Skip(1), result => Assert.Equal("skip", result.Status));
        Assert.Equal(loggedIn, profile.ModelsSyncedAt is not null);
    }
    private sealed class WaitingHandler : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            await Task.Delay(Timeout.Infinite, ct);
            return new HttpResponseMessage(HttpStatusCode.OK);
        }
    }
    [Fact]
    public async Task CancellationProducesFailureEntryWithoutHanging()
    {
        using var http = new HttpClient(new WaitingHandler());
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(50));
        var config = new AppConfig { Providers = [new() { Id = "slow", BaseUrl = "https://test/v1" }] };
        var results = await StartupModelSync.RunAsync(config, new(http, new Secrets(), new Auth(false)), cancellation.Token).WaitAsync(TimeSpan.FromSeconds(2));
        Assert.Equal("fail", Assert.Single(results).Status);
    }
    [Fact]
    public async Task FailedDiscoveryRetainsCachedModelsAndReportsFailure()
    {
        using var http = new HttpClient(new Handler());
        var config = new AppConfig { Providers = [new() { Id = "api", BaseUrl = "https://test/v1", AvailableModels = ["cached"] }] };
        var result = Assert.Single(await StartupModelSync.RunAsync(config, new(http, new Secrets(), new Auth(false))));
        Assert.Equal("fail", result.Status);
        Assert.Equal(1, result.ModelCount);
    }
}
