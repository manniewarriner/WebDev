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
public sealed partial class ManagedTeamTests
{
    private sealed class Fake(string model, Func<ChatRequest,string> answer, bool fail=false) : IModelProvider {
        public string Id=>model=="opus" || model=="haiku" ? "claude-outlook" : model=="qwen" ? "local-pc" : "opencode-go"; public string ModelId=>model; public string DisplayName=>model; public bool IsLocal=>model=="qwen"; public bool IsAgentic=>false;
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
        Assert.Equal("done",job.Status); Assert.Equal(action,Assert.Single(job.Decisions).Decision); Assert.NotEmpty(job.Events);
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
        var runner=new TeamOrchestrator(()=>new AgentLoop(new ToolRegistry(),new PermissionGate(new PermissionPolicy(),null)),new(),new() {FirstTokenTimeoutSeconds=1,LocalFirstTokenTimeoutSeconds=1});
        var job=await runner.RunAsync("task",dir.Path,pm,[new(worker,["*"],["*"])],false,"",default,verify:false);
        Assert.Equal("done",job.Status); Assert.Contains("timeout",Assert.Single(job.Decisions).Reason); Assert.Single(job.Failures); Assert.Equal(2,worker.Calls);
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

    [Theory]
    [InlineData("model unavailable")][InlineData("not in subscription")][InlineData("free tier")][InlineData("wrong endpoint")]
    public async Task PlanErrorsAreHiddenWithoutBackoff(string message) {
        var config=new ProviderConfig {BaseUrl="https://go/v1",Model="unlisted",AvailableModels=["bad"]};
        int backoffs=0;
        using var http=new HttpClient(new Handler(_=>new(HttpStatusCode.TooManyRequests) {Content=new StringContent(message)}));
        await ModelProbe.RunAsync(config,m=>new OpenAiCompatProvider("go","Go",new("https://go/v1",m),false,http),rateLimited:(_,_)=>backoffs++);
        Assert.True(config.ModelVerifications["bad"].Hidden); Assert.Equal(0,backoffs);
        Assert.False(config.ModelVerifications.ContainsKey("unlisted"));
    }
    [Fact] public async Task FailedAlternateEndpointIsHidden() {
        var config=new ProviderConfig {BaseUrl="https://go/v1",AvailableModels=["zen"],ModelBaseUrls=new() {["zen"]="https://zen/v1",["unlisted"]="https://zen/v1"}};
        using var http=new HttpClient(new Handler(_=>new(HttpStatusCode.ServiceUnavailable)));
        await ModelProbe.RunAsync(config,m=>new OpenAiCompatProvider("go","Go",new("https://zen/v1",m),false,http));
        Assert.True(config.ModelVerifications["zen"].Hidden); Assert.Single(config.ModelVerifications);
    }
    [Fact] public async Task GenuineRateLimitRetainsVerificationAndBacksOff() {
        var config=new ProviderConfig {AvailableModels=["glm"],ModelVerifications=new() {["glm"]=new(true,false,DateTimeOffset.MinValue,"Verified")}};
        int backoffs=0;
        using var http=new HttpClient(new Handler(_=>new(HttpStatusCode.TooManyRequests) {Content=new StringContent("too many requests")}));
        await ModelProbe.RunAsync(config,m=>new OpenAiCompatProvider("go","Go",new("https://go/v1",m),false,http),rateLimited:(_,_)=>backoffs++);
        Assert.False(config.ModelVerifications["glm"].Hidden); Assert.True(config.ModelVerifications["glm"].Verified); Assert.Equal(1,backoffs);
    }
    private sealed class WritingFake : IModelProvider {
        public string Id=>"writer"; public string ModelId=>"writer"; public string DisplayName=>"writer"; public bool IsLocal=>false; public bool IsAgentic=>false;
        public int Calls; public string WritePath="denied.txt";
        public Task<ProviderHealth> CheckHealthAsync(CancellationToken ct)=>Task.FromResult(ProviderHealth.Online);
        public async IAsyncEnumerable<ChatEvent> StreamChatAsync(ChatRequest request,[EnumeratorCancellation] CancellationToken ct) {
            Calls++; await Task.Yield(); yield return new ToolCallRequested(new("write","write_file",System.Text.Json.JsonSerializer.Serialize(new {path=WritePath,content="test"}))); yield return new Done();
        }
    }
    [Theory][InlineData(true)][InlineData(false)]
    public async Task DeniedWriteStopsWorkerAndReportsToPmWithoutRetry(bool unattended) {
        using var dir=new TempDir(); var worker=new WritingFake(); bool reported=false;
        var pm=new Fake("opus",r=> {
            var prompt=r.Messages.Last().Content!;
            if(prompt.StartsWith("Split")) return "[{\"id\":\"a\",\"title\":\"A\",\"skill\":\"code\",\"size\":\"small\",\"deps\":[],\"files\":[]}]";
            Assert.Contains("write denied",prompt); reported=true; return "Report denied permission to user";
        });
        var runner=new TeamOrchestrator(()=>new AgentLoop(ToolRegistry.CreateDefault(),new PermissionGate(new PermissionPolicy {WorkspacePermission=WorkspacePermission.Manual,Unattended=unattended},null)),new(),new());
        await Assert.ThrowsAsync<InvalidOperationException>(()=>runner.RunAsync("task",dir.Path,pm,[new(worker,["*"],["*"])],false,"",default,false));
        Assert.True(reported); Assert.Equal(1,worker.Calls); Assert.Contains("write denied",Assert.Single(runner.Job.Failures).Reason,StringComparison.OrdinalIgnoreCase); Assert.False(File.Exists(Path.Combine(dir.Path,"denied.txt")));
    }
    [Fact] public async Task OutsideWorkspaceDenialPreservesReasonAndPmRetriesInsideWorkspace() {
        using var dir=new TempDir(); var worker=new WritingFake {WritePath=Path.Combine(Path.GetTempPath(),"outside-"+Guid.NewGuid()+".txt")};
        var pm=new Fake("opus",r=> {
            var prompt=r.Messages.Last().Content!;
            if(prompt.StartsWith("Split")) return "[{\"id\":\"a\",\"title\":\"A\",\"skill\":\"code\",\"size\":\"small\",\"deps\":[],\"files\":[]}]";
            if(prompt.StartsWith("Worker failure")) {
                Assert.Contains("outside workspace",prompt); Assert.DoesNotContain("permissions require approval",prompt);
                return "{\"action\":\"reassign\",\"model\":\"qwen\",\"reason\":\"Use workspace temporary file\",\"instruction\":\"Keep writes inside workspace\"}";
            }
            return "PM summary";
        });
        var replacement=new Fake("qwen",r=>r.ReadOnly?"PASS":"finished");
        var runner=new TeamOrchestrator(()=>new AgentLoop(ToolRegistry.CreateDefault(),new PermissionGate(new PermissionPolicy {WorkspacePermission=WorkspacePermission.FullAccess},null)),new(),new());
        var job=await runner.RunAsync("task",dir.Path,pm,[new(worker,["*"],["*"]),new(replacement,["*"],["*"])],false,"",default,false);
        Assert.Equal("done",job.Status); Assert.Equal("PM summary",job.Summary); Assert.Equal("claude-outlook/opus",job.Manager);
        var decision=Assert.Single(job.Decisions); Assert.Equal("reassign",decision.Decision); Assert.Equal("writer/writer",decision.From); Assert.Equal("local-pc/qwen",decision.To);
        Assert.Contains(job.Events,e=>e.Decisions.Length==1); Assert.Contains(job.Events,e=>e.WorkerSkips.Any(s=>s.StartsWith("Haiku:")));
        Assert.False(File.Exists(worker.WritePath));
    }
    [Fact] public async Task DeadlineCancelsWorkersAndPmSummarizesPartialResults() {
        using var dir=new TempDir(); var worker=new Fake("qwen",_=>"done") {Stall=true}; bool summarized=false;
        var pm=new Fake("opus",r=> {
            if(r.Messages.Last().Content!.StartsWith("Split")) return "[{\"id\":\"a\",\"title\":\"A\",\"skill\":\"code\",\"size\":\"small\",\"deps\":[],\"files\":[]}]";
            summarized=true; return "Partial summary";
        });
        var runner=new TeamOrchestrator(()=>new AgentLoop(new ToolRegistry(),new PermissionGate(new PermissionPolicy(),null)),new(),new() {JobTimeLimitMinutes=0.01});
        var job=await runner.RunAsync("task",dir.Path,pm,[new(worker,["*"],["*"])],false,"",default,false);
        Assert.Equal("timed out",job.Status); Assert.True(summarized); Assert.Equal(1,worker.Calls); Assert.Equal(20,new TeamConfig().JobTimeLimitMinutes);
    }
    [Fact] public async Task PcGoAndHaikuAllReceiveChunksEvenWhenPmRepeatsOneModel() {
        using var dir=new TempDir(); var seen=new HashSet<string>();
        Fake Worker(string model)=>new(model,r=>{if(!r.ReadOnly) lock(seen) seen.Add(model); return r.ReadOnly?"PASS":"finished";});
        var pc=Worker("qwen"); var go=Worker("glm"); var secondGo=Worker("flash"); var haiku=Worker("haiku");
        var pm=new Fake("opus",r=>r.Messages.Last().Content!.StartsWith("Split") ? System.Text.Json.JsonSerializer.Serialize(Enumerable.Range(1,3).Select(i=>new {id=i.ToString(),title="Chunk "+i,skill="code",size="small",deps=Array.Empty<string>(),files=Array.Empty<string>(),model="glm"})) : "Summary");
        Assert.True(TeamRoles.IsWorker(pc));
        var runner=new TeamOrchestrator(()=>new AgentLoop(new ToolRegistry(),new PermissionGate(new PermissionPolicy(),null)),new(),new());
        await runner.RunAsync("task",dir.Path,pm,[new(pc,["*"],["*"]),new(go,["*"],["*"]),new(secondGo,["*"],["*"]),new(haiku,["*"],["*"])],false,"",default,false);
        Assert.Equal(3,seen.Count); Assert.Contains("qwen",seen); Assert.Contains("haiku",seen);
    }
    [Theory][InlineData(262144)][InlineData(32768)]
    public async Task MismatchedLoadedContextIsUnloadedAndReloaded(int context) {
        var paths=new List<string>(); var bodies=new List<string>(); int loads=0;
        using var http=new HttpClient(new AsyncHandler(async r=> {
            paths.Add(r.RequestUri!.AbsolutePath); bodies.Add(await r.Content!.ReadAsStringAsync());
            var json=r.RequestUri.AbsolutePath.EndsWith("/unload") ? "{}" : r.RequestUri.AbsolutePath.EndsWith("/load") ? "{\"instance_id\":\"instance\",\"load_config\":{\"context_length\":"+(++loads==1?context:65536)+"}}" : "{\"choices\":[{\"message\":{\"content\":\"OK\"}}]}";
            return new(HttpStatusCode.OK) {Content=new StringContent(json,System.Text.Encoding.UTF8,"application/json")};
        }));
        var provider=new OpenAiCompatProvider("pc","PC",new("http://localhost/v1","gemma",LmStudioLoad:true),true,http);
        await provider.StreamChatAsync(new([ChatMessage.User("hello")],[]),default).ToListAsync();
        Assert.Equal(new[]{"/api/v1/models/load","/api/v1/models/unload","/api/v1/models/load","/v1/chat/completions"},paths);
        Assert.Contains("65536",bodies[0]); Assert.Contains("65536",bodies[2]); Assert.Contains("instance",bodies[1]);
    }

    private sealed class FakeChild : IChildProcess {
        public int Kills, Disposals;
        public void KillTree()=>Kills++;
        public void Dispose()=>Disposals++;
    }
    [Fact] public void ProcessOwnershipStopsAndDisposesOnOperationEndAndHostExit() {
        using var registry=new ChildProcessRegistry(); var first=new FakeChild(); var second=new FakeChild();
        var ended=registry.Track(first); var running=registry.Track(second);
        ended.Dispose(); Assert.Equal(1,first.Kills); Assert.Equal(1,first.Disposals); Assert.Equal(0,second.Kills);
        registry.Dispose(); running.Dispose(); ended.Dispose();
        Assert.Equal(1,first.Kills); Assert.Equal(1,second.Kills); Assert.Equal(1,second.Disposals);
    }
    [Theory][InlineData("Permission denied")][InlineData("Claude requested permission to use Write, but you haven't granted it yet.")]
    public void NativeCliPermissionFailuresAreRecognized(string message) {
        var parsed=ClaudeStreamParser.Parse(System.Text.Json.JsonSerializer.Serialize(new {type="user",message=new {content=new[] {new {type="tool_result",tool_use_id="write",content=message,is_error=true}}}}),"claude");
        Assert.True(AgentLoop.IsWriteDenied(Assert.Single(parsed.Events)));
    }
    [Fact] public void FullAccessJsonUsesNormalizedFolderKeyAndStringEnum() {
        using var dir=new TempDir(); var config=new AppConfig(); config.SetPermission(dir.Path,WorkspacePermission.FullAccess);
        var json=System.Text.Json.JsonSerializer.Serialize(config,ConfigStore.Json);
        using var doc=System.Text.Json.JsonDocument.Parse(json);
        Assert.Equal("FullAccess",doc.RootElement.GetProperty("workspacePermissions").GetProperty(AppConfig.WorkspaceKey(dir.Path)).GetString());
        var restored=System.Text.Json.JsonSerializer.Deserialize<AppConfig>(json,ConfigStore.Json)!;
        Assert.Equal(WorkspacePermission.FullAccess,restored.PermissionFor(dir.Path+Path.DirectorySeparatorChar));
    }
}
