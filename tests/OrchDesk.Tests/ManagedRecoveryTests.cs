using System.Net;
using System.Text;
using OrchDesk.Core.Agent;
using OrchDesk.Core.Config;
using OrchDesk.Core.Integrations;
using OrchDesk.Core.Models;
using OrchDesk.Core.Orchestration;
using OrchDesk.Core.Permissions;
using OrchDesk.Core.Providers;
using OrchDesk.Core.Tools;

namespace OrchDesk.Tests;

public sealed partial class ManagedTeamTests
{
    [Fact]
    public async Task LocalFailedProbeIsRetriedAfterLoadAndReasoningVerifies()
    {
        var paths = new List<string>();
        using var http = new HttpClient(new AsyncHandler(async request => {
            paths.Add(request.RequestUri!.AbsolutePath);
            var body = await request.Content!.ReadAsStringAsync();
            if (request.RequestUri.AbsolutePath.EndsWith("/load"))
                return new(HttpStatusCode.OK) { Content = new StringContent("{}") };
            Assert.Contains("256", body);
            return new(HttpStatusCode.OK) { Content = new StringContent("data: {\"choices\":[{\"delta\":{\"reasoning_content\":\"Thinking\"}}]}\n\ndata: [DONE]\n\n", Encoding.UTF8, "text/event-stream") };
        }));
        var config = new ProviderConfig { IsLocal = true, AvailableModels = ["qwen"], ModelVerifications = new() { ["qwen"] = new(false, true, DateTimeOffset.UtcNow, "HTTP 400") } };
        IModelProvider Create(string model) => new OpenAiCompatProvider("local-pc", "PC", new("http://localhost:1234/v1", model, LmStudioLoad: true), true, http);
        await ModelProbe.RunAsync(config, Create);
        Assert.True(config.ModelVerifications["qwen"].Verified);
        Assert.False(config.ModelVerifications["qwen"].Hidden);
        Assert.Equal(new[] { "/api/v1/models/load", "/v1/chat/completions" }, paths);
        Assert.Equal(ProviderHealth.Online, await Create("qwen").CheckHealthAsync(default));
    }

    [Fact]
    public void ShellAndCliUseWorkspaceTemporaryDirectories()
    {
        using var dir = new TempDir();
        foreach (var start in new[] { ProcessRunner.Shell("pytest", dir.Path), CliRunner.CreateStartInfo("fake", [], dir.Path) }) {
            var root = Path.Combine(dir.Path, ".orchdesk-tmp");
            foreach (var name in new[] { "TMP", "TEMP", "TMPDIR" }) Assert.Equal(root, start.Environment[name]);
            Assert.Equal("--basetemp=\"" + Path.Combine(root, "pytest") + "\"", start.Environment["PYTEST_ADDOPTS"]);
            Assert.True(Directory.Exists(Path.Combine(root, "pytest")));
            Assert.Contains("*", File.ReadAllText(Path.Combine(root, ".gitignore")));
        }
        WorkspaceTemp.Clean(dir.Path);
        Assert.False(Directory.Exists(Path.Combine(dir.Path, ".orchdesk-tmp")));
    }

    [Fact]
    public async Task RepeatedFailureRejectsSecondRetryAndAsksPmForReassignment()
    {
        using var dir = new TempDir();
        var worker = new WritingFake { WritePath = Path.Combine(Path.GetTempPath(), Guid.NewGuid()+".txt") };
        int decisions = 0;
        var replacement = new Fake("qwen", r => r.ReadOnly ? "PASS" : "finished");
        var pm = new Fake("opus", r => {
            var prompt = r.Messages.Last().Content!;
            if (prompt.StartsWith("Split")) return "[{\"id\":\"a\",\"title\":\"A\",\"skill\":\"code\",\"size\":\"small\",\"deps\":[],\"files\":[]}]";
            if (prompt.StartsWith("Worker failure")) {
                decisions++;
                if (decisions <= 2) return "{\"action\":\"retry\",\"model\":\"writer\",\"reason\":\"retry\"}";
                Assert.Contains("Rejected retry", prompt);
                return "{\"action\":\"reassign\",\"model\":\"qwen\",\"reason\":\"different worker\"}";
            }
            return "Summary";
        });
        // Only the failing worker matches the initial chunk; replacement remains available to recovery.
        var runner = new TeamOrchestrator(() => new AgentLoop(ToolRegistry.CreateDefault(), new PermissionGate(new PermissionPolicy { WorkspacePermission = WorkspacePermission.FullAccess }, null)), new(), new());
        var job = await runner.RunAsync("task", dir.Path, pm, [new(worker,["*"],["*"]),new(replacement,["recovery"],["*"])], false, "", default, false);
        Assert.Equal("done", job.Status);
        Assert.Equal(2, worker.Calls);
        Assert.Equal(3, decisions);
        Assert.Equal(1, job.Decisions.Count(d => d.Decision == "retry"));
    }

    [Fact]
    public async Task OptionalFailedChunkReturnsSuccessfulPartialSummaryAndCleansTemp()
    {
        using var dir = new TempDir();
        var worker = new Fake("qwen", r => r.Messages.Last().Content!.Contains("Chunk: Verify") ? throw new InvalidOperationException("test failure") : "finished");
        var pm = new Fake("opus", r => r.Messages.Last().Content!.StartsWith("Split")
            ? "[{\"id\":\"a\",\"title\":\"Required\",\"skill\":\"code\",\"size\":\"small\",\"deps\":[],\"files\":[]},{\"id\":\"v\",\"title\":\"Verify\",\"skill\":\"code\",\"size\":\"small\",\"deps\":[\"a\"],\"files\":[],\"optional\":true}]"
            : r.Messages.Last().Content!.StartsWith("Worker failure") ? "{\"action\":\"retry\",\"model\":\"qwen\",\"reason\":\"retry\"}" : "Required work delivered");
        ProcessRunner.Shell("fake", dir.Path);
        var runner = new TeamOrchestrator(() => new AgentLoop(new ToolRegistry(), new PermissionGate(new PermissionPolicy(), null)), new(), new());
        var job = await runner.RunAsync("task", dir.Path, pm, [new(worker,["*"],["*"])], false, "", default, false);
        Assert.Equal("done", job.Status);
        Assert.Contains("Partial result", job.Summary);
        Assert.Contains("test failure", job.Summary);
        Assert.Contains(job.Cards, c => c.Id == "a" && c.Status == "done");
        Assert.False(Directory.Exists(Path.Combine(dir.Path, ".orchdesk-tmp")));
    }
}
