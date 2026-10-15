using System.Net;
using System.Text.Json;
using OrchDesk.Core.Config;
using OrchDesk.Core.Integrations;
using OrchDesk.Core.Models;
using OrchDesk.Core.Providers;
using OrchDesk.Core.Verification;
using Xunit;

namespace OrchDesk.Tests;

public sealed class ModelSyncTests
{
    private sealed class Secrets : ISecretProtector
    {
        public string Protect(string value) => "protected:" + value;
        public string Unprotect(string value) => value[10..];
    }
    private sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> reply) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) => Task.FromResult(reply(request));
    }
    private sealed class Auth(bool authenticated) : IClaudeAuthRunner
    {
        public ProviderConfig? Profile { get; private set; }
        public Task<bool> IsLoggedInAsync(ProviderConfig profile, CancellationToken ct) { Profile = profile; return Task.FromResult(authenticated); }
    }
    [Fact]
    public async Task OpenCodeMergesGoAndZenWithBearerKey()
    {
        var paths = new List<string>();
        using var http = new HttpClient(new Handler(request =>
        {
            paths.Add(request.RequestUri!.AbsolutePath);
            Assert.Equal("Bearer", request.Headers.Authorization?.Scheme);
            Assert.Equal("test-key", request.Headers.Authorization?.Parameter);
            return new(HttpStatusCode.OK) { Content = new StringContent(paths.Count == 1
                ? "{\"data\":[{\"id\":\"go-model\"},{\"id\":\"shared-free\"}]}"
                : "{\"data\":[{\"id\":\"zen-model\"},{\"id\":\"shared-free\"}]}") };
        }));
        var provider = new ProviderConfig { Id = "opencode-go", BaseUrl = "https://opencode.ai/inference/go/v1", ApiKeyProtected = new Secrets().Protect("test-key") };
        var result = await new IntegrationModelSync(http, new Secrets(), new Auth(false)).RunAsync(provider);
        Assert.True(result.Ok);
        Assert.Equal(new[] { "/inference/go/v1/models", "/zen/v1/models" }, paths);
        Assert.Equal(new[] { "go-model", "shared-free", "zen-model" }, provider.AvailableModels);
        Assert.NotNull(provider.ModelsSyncedAt);
        Assert.Equal("https://opencode.ai/zen/v1", provider.ModelBaseUrls["zen-model"]);
    }
    [Fact]
    public async Task SelectedZenModelUsesZenEndpointWithoutChangingSavedModel()
    {
        using var http = new HttpClient(new Handler(request =>
        {
            Assert.Equal("https://opencode.ai/zen/v1/models", request.RequestUri!.AbsoluteUri);
            return new(HttpStatusCode.OK);
        }));
        var config = new ProviderConfig { Id = "opencode-go", BaseUrl = "https://opencode.ai/inference/go/v1", Model = "go-model",
            ModelBaseUrls = new() { ["zen-free"] = "https://opencode.ai/zen/v1" } };
        var provider = new ProviderFactory(new Secrets(), http).Create(config, "zen-free")!;
        Assert.Equal(ProviderHealth.Online, await provider.CheckHealthAsync(default));
        Assert.Equal("go-model", config.Model);
        Assert.Equal("https://opencode.ai/inference/go/v1", config.BaseUrl);
    }
    [Theory]
    [InlineData(true)] [InlineData(false)]
    public async Task ClaudeSyncRequiresAuthenticatedProfile(bool authenticated)
    {
        using var http = new HttpClient();
        var auth = new Auth(authenticated);
        var config = new ProviderConfig { Kind = ProviderKind.ClaudeCli, ClaudeConfigDir = "profile-test" };
        var result = await new IntegrationModelSync(http, new Secrets(), auth).RunAsync(config);
        Assert.Equal(authenticated, result.Ok);
        Assert.Same(config, auth.Profile);
        Assert.Equal(authenticated ? new[] { "sonnet", "opus", "haiku" } : [], config.AvailableModels);
        Assert.Equal(authenticated, config.ModelsSyncedAt.HasValue);
    }
    [Fact]
    public void ClaudeAuthCommandIsNonInteractiveAndStripsPaidApiEnvironment()
    {
        var start = ClaudeAuthRunner.CreateAuthStartInfo("claude", "profile-test");
        Assert.Equal(new[] { "auth", "status", "--json" }, start.ArgumentList);
        Assert.Equal("profile-test", start.Environment["CLAUDE_CONFIG_DIR"]);
        foreach (var name in CliRunner.PaidApiEnvVars) Assert.False(start.Environment.ContainsKey(name));
        Assert.False(start.UseShellExecute);
        Assert.True(start.CreateNoWindow);
    }
    [Fact]
    public async Task TestDoesNotChangeSyncedModels()
    {
        using var http = new HttpClient();
        var config = new ProviderConfig { Kind = ProviderKind.ClaudeCli, AvailableModels = ["old"] };
        Assert.True((await new IntegrationModelSync(http, new Secrets(), new Auth(true)).RunAsync(config, false)).Ok);
        Assert.Equal(new[] { "old" }, config.AvailableModels);
        Assert.Null(config.ModelsSyncedAt);
    }
    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task FailedSyncPreservesModelsAndDoesNotExposeResponseOrException(bool throws)
    {
        using var http = new HttpClient(new Handler(_ => throws ? throw new HttpRequestException("sensitive-response")
            : new(HttpStatusCode.Unauthorized) { Content = new StringContent("sensitive-response") }));
        var config = new ProviderConfig { BaseUrl = "https://opencode.ai/inference/go/v1", ApiKeyProtected = new Secrets().Protect("test-key"), AvailableModels = ["old"] };
        var result = await new IntegrationModelSync(http, new Secrets(), new Auth(false)).RunAsync(config);
        Assert.False(result.Ok);
        Assert.DoesNotContain("sensitive-response", result.Message);
        Assert.DoesNotContain("test-key", result.Message);
        Assert.Equal(new[] { "old" }, config.AvailableModels);
        Assert.Null(config.ModelsSyncedAt);
    }
    [Fact]
    public void PickerExpandsOnlyEnabledProvidersAndKeepsLocalAndRemoteEntries()
    {
        var config = AppConfig.CreateDefault();
        config.Providers.Single(p => p.Id == "claude-gmail").AvailableModels = ["sonnet", "opus", "haiku"];
        var options = ModelPickerOption.Expand(config.Providers);
        Assert.Equal(3, options.Count(o => o.Id == "claude-gmail"));
        Assert.Contains(options, o => o.Label == "Claude (Gmail) · Claude Sonnet");
        Assert.Single(options.Where(o => o.Id == "local-pc" && o.Model is null));
        Assert.DoesNotContain(options, o => o.Id == "remote-pc");
        config.Providers.Single(p => p.Id == "remote-pc").Enabled = true;
        Assert.Single(ModelPickerOption.Expand(config.Providers).Where(o => o.Id == "remote-pc"));
    }
    [Fact]
    public void FactoryOverridesModelWithoutMutatingSavedProvider()
    {
        using var http = new HttpClient();
        var config = new ProviderConfig { Kind = ProviderKind.ClaudeCli, Model = "sonnet", Enabled = true };
        var provider = Assert.IsType<ClaudeCliProvider>(new ProviderFactory(new Secrets(), http).Create(config, "opus"));
        Assert.Contains("opus", provider.BuildArgs(new ChatRequest([], []), null));
        Assert.Equal("sonnet", config.Model);
    }
    [Fact]
    public void OldConfigLoadsAndSyncedModelsRoundTrip()
    {
        var config = JsonSerializer.Deserialize<AppConfig>("{\"providers\":[{\"id\":\"old\",\"model\":\"saved\"}]}", ConfigStore.Json)!;
        var provider = Assert.Single(config.Providers);
        Assert.Empty(provider.AvailableModels);
        Assert.Null(provider.ModelsSyncedAt);
        provider.AvailableModels = ["sonnet"];
        provider.ModelsSyncedAt = DateTimeOffset.UtcNow;
        var loaded = JsonSerializer.Deserialize<AppConfig>(JsonSerializer.Serialize(config, ConfigStore.Json), ConfigStore.Json)!;
        Assert.Equal(provider.AvailableModels, loaded.Providers[0].AvailableModels);
        Assert.Equal(provider.ModelsSyncedAt, loaded.Providers[0].ModelsSyncedAt);
    }
    [Fact]
    public void SmokeSummaryIncludesIntegrationCounts()
    {
        var config = new AppConfig { Providers = [new() { Kind = ProviderKind.ClaudeCli, AvailableModels = ["sonnet", "opus"] }, new() { Id = "opencode-go", AvailableModels = ["free"] }] };
        Assert.Equal("Claude: 2 models; OpenCode: 1 models", IntegrationSmokeRunner.IntegrationSummary(config));
    }
}
