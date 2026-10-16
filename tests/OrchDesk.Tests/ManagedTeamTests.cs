using System.Net;
using System.Runtime.CompilerServices;
using OrchDesk.Core.Agent;
using OrchDesk.Core.Config;
using OrchDesk.Core.Integrations;
using OrchDesk.Core.Models;
using OrchDesk.Core.Orchestration;
using OrchDesk.Core.Permissions;
using OrchDesk.Core.Providers;
using OrchDesk.Core.Tools;
namespace OrchDesk.Tests;
public sealed class ManagedTeamTests
{
    private sealed class Fake(string model, Func<ChatRequest,string> answer, bool fail=false) : IModelProvider {
        public string Id=>model=="opus"?"claude-outlook":"opencode-go"; public string ModelId=>model; public string DisplayName=>model; public bool IsLocal=>false; public bool IsAgentic=>false;
        public int Calls; public bool Fail=fail; public bool Stall;
        public Task<ProviderHealth> CheckHealthAsync(CancellationToken ct)=>Task.FromResult(ProviderHealth.Online);
        public async IAsyncEnumerable<ChatEvent> StreamChatAsync(ChatRequest request,[EnumeratorCancellation] CancellationToken ct) {
            Calls++; await Task.Yield();
            if(Stall) {Stall=false; await Task.Delay(Timeout.Infinite,ct);}
            if(Fail) {Fail=false; yield return new TextDelta("partial work"); yield return new ProviderUnavailable(Id,"context overflow"); yield break;}
            yield return new TextDelta(answer(request)); yield return new Done();
        }
    }
    [Theory]
    [InlineData("retry")][InlineData("reassign")][InlineData("replan")]
    public async Task WorkerRecoveryIsDecidedByReadOnlyPmAndCarriesPartialOutput(string action) {
        using var dir=new TempDir();
        var a=new Fake("qwen",r=>r.ReadOnly?"PASS":"finished",true);
        var b=new Fake("glm",r=>r.ReadOnly?"PASS":"finished");
        var pm=new Fake("opus",r=> {
            Assert.True(r.ReadOnly); Assert.DoesNotContain(r.Tools,t=>t.Name is "write_file" or "run_command" or "edit_file");
            var prompt=r.Messages.Last().Content!;
            if(prompt.StartsWith("Split")) return "[{\"id\":\"a\",\"title\":\"Layout\",\"skill\":\"code\",\"size\":\"small\",\"deps\":[],\"files\":[],\"model\":\"qwen\"},{\"id\":\"b\",\"title\":\"Docs\",\"skill\":\"code\",\"size\":\"small\",\"deps\":[\"a\"],\"files\":[],\"model\":\"glm\"}]";
            if(prompt.StartsWith("Worker failure")) {
                Assert.Contains("partial work",prompt); Assert.Contains("context overflow",prompt);
                return System.Text.Json.JsonSerializer.Serialize(new {action,model=action=="retry"?"qwen":"glm",reason="context overflow",instruction="Implement a smaller chunk, retain partial work"});
            }
            return "Summary";
        });
        var runner=new TeamOrchestrator(()=>new AgentLoop(ToolRegistry.CreateDefault(),new PermissionGate(new PermissionPolicy {WorkspaceRoot=dir.Path,WorkspacePermission=WorkspacePermission.FullAccess},null)),new(),new());
        var job=await runner.RunAsync("build",dir.Path,pm,[new(a,["*"],["*"]),new(b,["*"],["*"])],false,"dotnet --version",default);
        Assert.Equal("done",job.Status); Assert.Contains(action,Assert.Single(job.Decisions)); Assert.NotEmpty(job.Events);
    }
    [Fact] public async Task StalledWorkerIsReportedToPmForRetry() {
        using var dir=new TempDir();
        var worker=new Fake("qwen",_=>"Done") {Stall=true};
        var pm=new Fake("opus",r=> {
            var prompt=r.Messages.Last().Content!;
            if(prompt.StartsWith("Split")) return "[{\"id\":\"a\",\"title\":\"A\",\"skill\":\"code\",\"size\":\"small\",\"deps\":[],\"files\":[]}]";
            if(prompt.StartsWith("Worker failure")) return "{\"action\":\"retry\",\"model\":\"qwen\",\"reason\":\"timeout\",\"instruction\":\"try again\"}";
            return "Summary";
        });
        var runner=new TeamOrchestrator(()=>new AgentLoop(new ToolRegistry(),new PermissionGate(new PermissionPolicy(),null)),new(),new() {FirstTokenTimeoutSeconds=1});
        var job=await runner.RunAsync("task",dir.Path,pm,[new(worker,["*"],["*"])],false,"",default,verify:false);
        Assert.Equal("done",job.Status); Assert.Contains("timeout",Assert.Single(job.Decisions)); Assert.Single(job.Failures); Assert.Equal(2,worker.Calls);
    }
    [Fact] public async Task PmCanSplitFailedChunkIntoSmallerAssignedChunks() {
        using var dir=new TempDir();
        var worker=new Fake("qwen",r=>r.ReadOnly?"PASS":"smaller chunk",true);
        var pm=new Fake("opus",r=> {
            var prompt=r.Messages.Last().Content!;
            if(prompt.StartsWith("Split")) return "[{\"id\":\"a\",\"title\":\"Large chunk\",\"skill\":\"code\",\"size\":\"small\",\"deps\":[],\"files\":[]}]";
            if(prompt.StartsWith("Worker failure")) return "{\"action\":\"replan\",\"model\":\"qwen\",\"reason\":\"context overflow\",\"instruction\":\"split\",\"chunks\":[{\"title\":\"Small A\",\"model\":\"qwen\",\"instruction\":\"Do A with retained partial work\"},{\"title\":\"Small B\",\"model\":\"qwen\",\"instruction\":\"Do B\"}]}";
            return "Summary";
        });
        var runner=new TeamOrchestrator(()=>new AgentLoop(new ToolRegistry(),new PermissionGate(new PermissionPolicy(),null)),new(),new());
        var job=await runner.RunAsync("task",dir.Path,pm,[new(worker,["*"],["*"])],false,"",default,verify:false);
        Assert.Equal("done",job.Status); Assert.Equal(3,worker.Calls);
        Assert.Contains(job.Cards,c=>c.Title=="Small A" && c.Status=="done"); Assert.Contains(job.Cards,c=>c.Title=="Small B" && c.Status=="done");
    }
    private sealed class Handler(Func<HttpRequestMessage,HttpResponseMessage> response):HttpMessageHandler {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken ct)=>Task.FromResult(response(request));
    }
    [Fact] public async Task ContextGuardCountsSchemasAndDoesNotSendOversizedPrompt() {
        int calls=0;
        using var http=new HttpClient(new Handler(_=> {calls++; return new(HttpStatusCode.OK);}));
        var p=new OpenAiCompatProvider("pc","PC",new("http://localhost/v1","qwen",ContextLength:100),true,http);
        var events=await p.StreamChatAsync(new([ChatMessage.User(new string('x',350))],[]),default).ToListAsync();
        Assert.Contains(events,e=>e is ProviderUnavailable); Assert.Equal(0,calls);
        Assert.True(OpenAiCompatProvider.PromptTokens(new([], [new("tool",new string('x',400),new())]))>100);
    }
    private sealed class AsyncHandler(Func<HttpRequestMessage,Task<HttpResponseMessage>> response):HttpMessageHandler {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken ct)=>response(request);
    }
    [Fact] public async Task ExplicitLoadPrecedesChatAndUsesContextAndTtl() {
        var paths=new List<string>();
        string? loadBody=null;
        using var http=new HttpClient(new AsyncHandler(async r=> {
            paths.Add(r.RequestUri!.AbsolutePath);
            if(paths.Count==1) { loadBody=await r.Content!.ReadAsStringAsync(); return new(HttpStatusCode.OK){Content=new StringContent("{\"status\":\"loaded\",\"load_config\":{\"context_length\":65536}}")};}
            return new(HttpStatusCode.OK){Content=new StringContent("{\"choices\":[{\"message\":{\"content\":\"OK\"}}]}",System.Text.Encoding.UTF8,"application/json")};
        }));
        var p=new OpenAiCompatProvider("pc","PC",new("http://localhost/v1","qwen",LmStudioLoad:true),true,http);
        await p.StreamChatAsync(new([ChatMessage.User("hello")],[]),default).ToListAsync();
        Assert.Equal(new[]{"/api/v1/models/load","/v1/chat/completions"},paths);
        Assert.Contains("65536",loadBody); Assert.Contains("3600",loadBody);
    }
    [Theory][InlineData(400)][InlineData(403)][InlineData(404)]
    public async Task ProbeHidesUnsupportedModelsAndPersistsDailyResult(int status) {
        using var dir=new TempDir(); using var store=new ConfigStore(dir.Path);
        var c=new ProviderConfig {Id="go",AvailableModels=["bad"]}; int calls=0;
        using var http=new HttpClient(new Handler(_=>{calls++;return new((HttpStatusCode)status);}));
        IModelProvider Create(string model)=>new OpenAiCompatProvider("go","Go",new("https://test/v1",model),false,http);
        await ModelProbe.RunAsync(c,Create); Assert.True(c.ModelVerifications["bad"].Hidden);
        store.Save(new AppConfig {Providers=[c]}); c=store.Load().Providers.Single();
        await ModelProbe.RunAsync(c,Create); Assert.Equal(1,calls);
        await ModelProbe.RunAsync(c,Create,true); Assert.Equal(2,calls);
    }
}
