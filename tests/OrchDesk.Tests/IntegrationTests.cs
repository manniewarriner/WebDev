using System.Net;
using System.Text;
using OrchDesk.Core.Config;
using OrchDesk.Core.Integrations;
using Xunit;
namespace OrchDesk.Tests;
public class IntegrationTests
{
    private sealed class Secrets : ISecretProtector { public string Protect(string s) => s; public string Unprotect(string s) => s; }
    private sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> reply) : HttpMessageHandler {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) => Task.FromResult(reply(request));
    }
    [Theory]
    [InlineData("local-pc")][InlineData("local-gemma")][InlineData("remote-pc")][InlineData("ollama")][InlineData("opencode-go")]
    public async Task HttpProvidersTestModelsAndRealChatWithHeaders(string id)
    {
        var calls = new List<string>();
        using var http = new HttpClient(new Handler(r => {
            calls.Add(r.RequestUri!.AbsolutePath);
            Assert.Equal("client", r.Headers.GetValues("CF-Access-Client-Id").Single());
            Assert.Equal("secret", r.Headers.GetValues("CF-Access-Client-Secret").Single());
            if (r.Method == HttpMethod.Post) Assert.Equal("key", r.Headers.Authorization!.Parameter);
            return new(HttpStatusCode.OK) { Content = new StringContent(r.Method == HttpMethod.Get ? "{}" : "{\"choices\":[{\"message\":{\"content\":\"OK\"},\"finish_reason\":\"stop\"}]}", Encoding.UTF8, "application/json") };
        }));
        var result = await new ConnectionTester(http, new Secrets()).TestAsync(new() { Id = id, BaseUrl = "https://relay.test/v1", Model = "qwen", ApiKeyProtected = "key", AccessClientIdProtected = "client", AccessClientSecretProtected = "secret" });
        Assert.True(result.Ok); Assert.Equal(new[] { "/v1/models", "/v1/chat/completions" }, calls);
    }
    [Fact] public async Task FailureNeverLeaksCredentials()
    {
        using var http = new HttpClient(new Handler(_ => throw new HttpRequestException("super-secret")));
        var result = await new ConnectionTester(http, new Secrets()).TestWebAsync(true);
        Assert.False(result.Ok); Assert.DoesNotContain("super-secret", result.Message);
    }
    [Fact] public async Task WebAndMeshyUseExpectedEndpoints()
    {
        var urls = new List<string>();
        using var http = new HttpClient(new Handler(r => { urls.Add(r.RequestUri!.Host); if (r.RequestUri.Host == "api.meshy.ai") Assert.Equal("key", r.Headers.Authorization!.Parameter); return new(HttpStatusCode.OK); }));
        var tester = new ConnectionTester(http, new Secrets());
        Assert.True((await tester.TestWebAsync(true)).Ok); Assert.True((await tester.TestMeshyAsync("key")).Ok);
        Assert.Equal(new[] { "example.com", "api.meshy.ai" }, urls);
        Assert.False((await tester.TestWebAsync(false)).Ok); Assert.False((await tester.TestMeshyAsync(null)).Ok);
    }
    [Theory][InlineData(ProviderKind.ClaudeCli)][InlineData(ProviderKind.CodexCli)][InlineData(ProviderKind.OpenCodeCli)]
    public async Task MissingCliFailsGracefully(ProviderKind kind)
    {
        using var http = new HttpClient();
        Assert.False((await new ConnectionTester(http, new Secrets()).TestAsync(new() { Kind = kind, Executable = "orchdesk-missing-cli" })).Ok);
    }
    [Fact] public async Task ConfigRemovesBuiltInProvidersPersistentlyOnReload()
    {
        using var dir = new TempDir(); using var store = new ConfigStore(dir.Path);
        var original = AppConfig.CreateDefault();
        Assert.Contains(original.Providers, p => p.Id == "local-pc");

        // Remove a built-in provider and save
        original.Providers.RemoveAll(p => p.Id == "local-pc");
        store.Save(original);

        // Reload and verify the removal persists (do not re-add missing providers)
        var loaded = store.Load();
        Assert.DoesNotContain(loaded.Providers, p => p.Id == "local-pc");
    }

    [Fact] public async Task ConfigNotifiesInAppAndExternalChanges()
    {
        using var dir = new TempDir(); using var store = new ConfigStore(dir.Path);
        var changed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        store.Watch(); store.Changed += () => changed.TrySetResult();
        store.Save(AppConfig.CreateDefault()); await changed.Task.WaitAsync(TimeSpan.FromSeconds(3));
        changed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await File.WriteAllTextAsync(store.FilePath, "{}"); await changed.Task.WaitAsync(TimeSpan.FromSeconds(3));
    }
}
