using System.Net;
using System.Text;
using OrchDesk.Core.Config;
using OrchDesk.Core.Automation;
using OrchDesk.Core.Knowledge;
using OrchDesk.Core.Telemetry;
using OrchDesk.Core.Dashboard;
using OrchDesk.Core.Research;
using OrchDesk.Core.Models;
using OrchDesk.Core.Tools;
using OrchDesk.Core.Permissions;
using OrchDesk.Core.Safety;
using OrchDesk.Core.Orchestration;
using OrchDesk.Core.Providers;
using Xunit;

namespace OrchDesk.Tests.Baseline;
public sealed class MeshyTests : IDisposable
{
    private const string Key = "test-secret-key-123";
    private readonly string root = Path.Combine(Path.GetTempPath(), "orchdesk-meshy-" + Guid.NewGuid().ToString("N"));
    public MeshyTests() => Directory.CreateDirectory(root);
    public void Dispose() { try { Directory.Delete(root, true); } catch (IOException) { } }

    private sealed class FakeHandler(params HttpResponseMessage[] replies) : HttpMessageHandler
    {
        private readonly Queue<HttpResponseMessage> queue = new(replies);
        public List<HttpRequestMessage> Requests { get; } = [];
        public List<string?> Bodies { get; } = [];
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request);
            Bodies.Add(request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken));
            return queue.Count > 0 ? queue.Dequeue() : throw new InvalidOperationException("Unexpected request: " + request.Method + " " + request.RequestUri);
        }
    }

    private static HttpResponseMessage Json(string body, HttpStatusCode code = HttpStatusCode.OK) => new(code) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
    private static HttpResponseMessage Bytes(byte[] data) => new(HttpStatusCode.OK) { Content = new ByteArrayContent(data) };

    private MeshyClient Client(FakeHandler handler, string? key = Key) => new(() => key, new HttpClient(handler)) { PollInterval = TimeSpan.FromMilliseconds(1) };

    [Fact]
    public async Task TextToImageSavesPngAndReportsProgress()
    {
        var png = new byte[] { 0x89, 0x50, 0x4E, 0x47, 1, 2, 3 };
        var handler = new FakeHandler(
            Json("""{"result":"task-1"}"""),
            Json("""{"status":"IN_PROGRESS","progress":45}"""),
            Json("""{"status":"SUCCEEDED","progress":100,"image_urls":["https://assets.meshy.ai/img/out.png"]}"""),
            Bytes(png));
        var messages = new List<string>();
        var path = await Client(handler).TextToImageAsync("a red car", root, messages.Add, CancellationToken.None);

        Assert.True(File.Exists(path));
        Assert.Equal(".png", Path.GetExtension(path));
        Assert.Equal(png, File.ReadAllBytes(path));
        Assert.Contains("Meshy 45%", messages);
        Assert.Contains("Meshy 100%", messages);
        Assert.Equal("Bearer " + Key, handler.Requests[0].Headers.Authorization!.ToString());
        Assert.Equal("https://api.meshy.ai/openapi/v1/text-to-image/task-1", handler.Requests[1].RequestUri!.ToString());
    }

    [Fact]
    public async Task ImageTo3DSendsDataUriAndSavesGlb()
    {
        var source = Path.Combine(root, "car.png");
        File.WriteAllBytes(source, [0x89, 0x50, 0x4E, 0x47]);
        var glb = new byte[] { (byte)'g', (byte)'l', (byte)'T', (byte)'F' };
        var handler = new FakeHandler(
            Json("""{"result":"task-2"}"""),
            Json("""{"status":"SUCCEEDED","progress":100,"model_urls":{"glb":"https://assets.meshy.ai/model.glb"}}"""),
            Bytes(glb));
        var path = await Client(handler).ImageTo3DAsync(source, Path.Combine(root, "models"), null, CancellationToken.None);

        Assert.Equal(".glb", Path.GetExtension(path));
        Assert.Equal(glb, File.ReadAllBytes(path));
        Assert.Contains("data:image/png;base64,", handler.Bodies[0]);
    }

    [Fact]
    public async Task MissingKeyThrowsWithoutCallingApi()
    {
        var handler = new FakeHandler();
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => Client(handler, key: "").TextToImageAsync("x", root, null, CancellationToken.None));
        Assert.Equal("Meshy API key not set. Add key id meshy in Settings.", ex.Message);
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task FailedStatusThrowsTaskErrorWithoutKey()
    {
        var handler = new FakeHandler(
            Json("""{"result":"task-3"}"""),
            Json("""{"status":"FAILED","progress":0,"task_error":{"message":"Prompt rejected"}}"""));
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => Client(handler).TextToImageAsync("x", root, null, CancellationToken.None));
        Assert.Contains("Prompt rejected", ex.Message);
        Assert.DoesNotContain(Key, ex.Message);
    }

    [Fact]
    public async Task RateLimitThrowsUsageLimit()
    {
        var handler = new FakeHandler(Json("{}", (HttpStatusCode)429));
        var ex = await Assert.ThrowsAsync<UsageLimitException>(() => Client(handler).TextToImageAsync("x", root, null, CancellationToken.None));
        Assert.True(ex.ResetAt > DateTimeOffset.UtcNow);
    }
}
