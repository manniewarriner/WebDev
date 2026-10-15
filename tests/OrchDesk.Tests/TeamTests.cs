using System.Runtime.CompilerServices;
using OrchDesk.Core.Agent;
using OrchDesk.Core.Models;
using OrchDesk.Core.Orchestration;
using OrchDesk.Core.Permissions;
using OrchDesk.Core.Providers;
using OrchDesk.Core.Tools;
namespace OrchDesk.Tests;
public sealed class TeamTests
{
    private sealed class Model(string model, Func<ChatRequest,IEnumerable<ChatEvent>> response, string id="go") : IModelProvider
    {
        public string Id=>id; public string ModelId=>model; public string DisplayName=>model; public bool IsLocal=>id=="pc"; public bool IsAgentic=>false;
        public List<ChatRequest> Requests { get; }=[];
        public Task<ProviderHealth> CheckHealthAsync(CancellationToken ct)=>Task.FromResult(ProviderHealth.Online);
        public async IAsyncEnumerable<ChatEvent> StreamChatAsync(ChatRequest request,[EnumeratorCancellation] CancellationToken ct) { lock(Requests) Requests.Add(request with { Messages=request.Messages.ToArray() }); await Task.Delay(30,ct); foreach(var ev in response(request)) yield return ev; }
    }
    private static IEnumerable<ChatEvent> Say(string text)=>[new TextDelta(text),new UsageReported(new(10,20)),new Done()];
    private static AgentLoop Loop(string root)=>new(ToolRegistry.CreateDefault(),new PermissionGate(new PermissionPolicy { WorkspaceRoot=root, WorkspacePermission=OrchDesk.Core.Config.WorkspacePermission.FullAccess },null));
    [Fact] public void TeamViewModelExposesPerModelRowsAndRefreshesBackoff()
    {
        var tracker = new ModelUsageTracker();
        var model = new Model("glm", _ => Say("ok"));
        tracker.Start(model); tracker.Complete(model, new(12, 8));
        var item = new OrchDesk.App.ViewModels.TeamItem();
        item.Refresh(new TeamJob(), tracker);
        var row = Assert.Single(item.ModelUsageRows);
        Assert.Equal("go/glm", row.Model);
        Assert.Equal(1, row.TodayRequests); Assert.Equal(20, row.TodayTokens);
        Assert.Equal(1, row.SessionRequests); Assert.Equal(20, row.SessionTokens);
        Assert.Equal("Ready", row.Backoff);
        tracker.Backoff(model); item.Refresh(new TeamJob(), tracker);
        Assert.StartsWith("Until ", Assert.Single(item.ModelUsageRows).Backoff);
    }
    [Fact] public void LeastUsedDailyResetPersistenceAndRetryAfter()
    {
        using var dir=new TempDir(); var now=DateTimeOffset.UtcNow; var tracker=new ModelUsageTracker(dir.Path,()=>now);
        var a=new Model("glm",_=>Say("ok")); var b=new Model("kimi",_=>Say("ok"));
        Assert.Same(a,tracker.Pick([a,b])); tracker.Start(a); tracker.Complete(a,new(3,4)); Assert.Same(b,tracker.Pick([a,b]));
        tracker.Backoff(b,TimeSpan.FromMinutes(10)); Assert.Same(a,tracker.Pick([a,b])); Assert.Equal(now.AddMinutes(10),tracker.Snapshot()[ModelUsageTracker.Key(b)].BackoffUntil);
        var loaded=new ModelUsageTracker(dir.Path,()=>now); Assert.Equal(7,loaded.Snapshot()[ModelUsageTracker.Key(a)].Tokens); Assert.Equal(0,loaded.Snapshot()[ModelUsageTracker.Key(a)].SessionRequests);
        now=now.AddDays(1); Assert.Empty(tracker.Snapshot()); Assert.Same(a,tracker.Pick([a,b]));
    }
    [Fact] public async Task AutoChatRotatesGoModelsAndReassigns429()
    {
        var a=new Model("glm",_=>Say("a")); var b=new Model("kimi",_=>Say("b")); var usage=new ModelUsageTracker(); var auto=new AutoChatProvider([a,b],usage);
        await auto.StreamChatAsync(new([ChatMessage.User("hi")],[]),default).ToListAsync(); await auto.StreamChatAsync(new([ChatMessage.User("hi")],[]),default).ToListAsync(); Assert.Single(a.Requests); Assert.Single(b.Requests);
        var limited=new Model("deepseek",_=>[new UsageLimitHit("go","limit") {HttpStatus=429,RetryAfter=TimeSpan.FromMinutes(12)}]);
        var events=await new AutoChatProvider([limited,b],new()).StreamChatAsync(new([ChatMessage.User("hi")],[]),default).ToListAsync(); Assert.Contains(events,e=>e is TextDelta {Text:"b"});
    }
    [Fact] public async Task PlanStopsAndPmHasNoWriteOrExecTools()
    {
        using var dir=new TempDir(); var pm=new Model("opus",r=> { Assert.True(r.ReadOnly); Assert.DoesNotContain(r.Tools,t=>t.Name is "write_file" or "edit_file" or "delete_file" or "run_command"); return Say("[{\"id\":\"a\",\"title\":\"A\",\"skill\":\"code\",\"size\":\"small\",\"deps\":[],\"files\":[]}]"); },"claude");
        var worker=new Model("kimi",_=>Say("PASS")); var runner=new TeamOrchestrator(()=>Loop(dir.Path),new(),new());
        var job=await runner.RunAsync("task",dir.Path,pm,[new(worker,["*"],["*"])],true,"dotnet test",default);
        Assert.Equal("awaiting approval",job.Status); Assert.Empty(worker.Requests);
    }
    [Fact] public async Task ParallelDelegationDependenciesCrossReviewAndAggregation()
    {
        using var dir=new TempDir(); var active=0; var peak=0; var completed=0;
        var pm=new Model("opus",r=>Say(r.Messages.Last().Content!.StartsWith("Split") ? "[{\"id\":\"a\",\"title\":\"A\",\"skill\":\"code\",\"size\":\"small\",\"deps\":[],\"files\":[]},{\"id\":\"b\",\"title\":\"B\",\"skill\":\"code\",\"size\":\"small\",\"deps\":[],\"files\":[]},{\"id\":\"c\",\"title\":\"C\",\"skill\":\"code\",\"size\":\"small\",\"deps\":[\"a\",\"b\"],\"files\":[]}]" : "Summary"),"claude");
        var a=new Model("glm",r=> {if(!r.ReadOnly) Interlocked.Increment(ref completed); return Say(r.ReadOnly?"PASS":"A result");}); var b=new Model("kimi",r=> { if(!r.ReadOnly) Interlocked.Increment(ref completed); return Say(r.ReadOnly?"PASS":"B result");});
        var runner=new TeamOrchestrator(()=>Loop(dir.Path),new(),new());
        runner.Changed+=job=> { var running=job.Cards.Count(c=>c.Status=="running"); peak=Math.Max(peak,running); active=running; };
        var job=await runner.RunAsync("task",dir.Path,pm,[new(a,["*"],["*"]),new(b,["*"],["*"])],false,"dotnet --version",default);
        Assert.Equal("done",job.Status); Assert.True(peak>=2); Assert.Equal(3,completed);
        Assert.Contains(a.Requests,r=>r.ReadOnly); Assert.Contains(b.Requests,r=>r.ReadOnly);
        var dependent=a.Requests.Concat(b.Requests).Single(r=>!r.ReadOnly&&r.Messages.Last().Content!.Contains("Chunk: C")); Assert.Contains("result",dependent.Messages.Last().Content!);
        Assert.Equal(job.Cards.Sum(c=>c.Usage.InputTokens),job.Usage.InputTokens); Assert.Equal(job.Cards.Sum(c=>c.Usage.OutputTokens),job.Usage.OutputTokens);
        Assert.Contains("tokens",job.ThoughtSummary);
    }
    [Fact] public void MixedUsageAggregationKeepsEstimateMarker()
    {
        var job=new TeamJob(); job.Cards.Add(new() {Usage=new(10,20),Elapsed=TimeSpan.FromSeconds(2)}); job.Cards.Add(new() {Usage=new(3,7,true),Elapsed=TimeSpan.FromSeconds(4)});
        Assert.Equal(new ResponseUsage(13,27,true),job.Usage); Assert.Contains("~40",job.ThoughtSummary); Assert.Contains("6s",job.ThoughtSummary);
    }
    [Fact] public async Task PmToolInjectionIsBlocked()
    {
        using var dir=new TempDir(); var pm=new Model("opus",_=>[new ToolCallRequested(new("x","write_file","{\"path\":\"evil\",\"content\":\"x\"}"))]);
        await Loop(dir.Path).RunAsync(pm,[ChatMessage.User("plan")],new(dir.Path),default,true).ToListAsync(); Assert.False(File.Exists(Path.Combine(dir.Path,"evil")));
    }
    [Fact] public void PcOffAndClaudeAbsentUsePaidGoPlannerAndWorkers()
    {
        var go=new Model("glm-5",_=>Say("ok"),"opencode-go"); var pc=new Model("qwen",_=>Say("ok"),"pc"); var haiku=new Model("haiku",_=>Say("ok"),"claude"); var opus=new Model("opus",_=>Say("ok"),"claude");
        Assert.Same(opus,TeamRoles.Planner([go,pc,haiku,opus],new())); Assert.Same(go,TeamRoles.Planner([go],new())); Assert.True(TeamRoles.IsWorker(go)); Assert.True(TeamRoles.IsWorker(haiku)); Assert.Null(TeamRoles.Planner([],new()));
    }
    [Fact] public async Task WorkerWritesUsePermissionGate()
    {
        using var dir=new TempDir(); var asks=0;
        var gate=new PermissionGate(new PermissionPolicy {WorkspaceRoot=dir.Path,WorkspacePermission=OrchDesk.Core.Config.WorkspacePermission.Manual},(r,c)=> {asks++; return Task.FromResult(ApprovalAnswer.Deny);});
        var loop=new AgentLoop(ToolRegistry.CreateDefault(),gate);
        var result=await loop.ExecuteToolAsync(new("x","write_file","{\"path\":\"denied.txt\",\"content\":\"x\"}"),new(dir.Path),default);
        Assert.True(result.IsError); Assert.Equal(1,asks); Assert.False(File.Exists(Path.Combine(dir.Path,"denied.txt")));
        result=await loop.ExecuteToolAsync(new("y","write_file","{\"path\":\"../outside.txt\",\"content\":\"x\"}"),new(dir.Path),default); Assert.True(result.IsError);
    }

    [Fact] public async Task TeamReassignsLimitedWorkerAndPlannerFallsBackReadOnly()
    {
        using var dir=new TempDir(); var usage=new ModelUsageTracker();
        var unavailable=new Model("opus",_=>[new ProviderUnavailable("claude","not logged in")],"claude");
        var plan="[{\"id\":\"a\",\"title\":\"A\",\"skill\":\"code\",\"size\":\"small\",\"deps\":[],\"files\":[]},{\"id\":\"b\",\"title\":\"B\",\"skill\":\"code\",\"size\":\"small\",\"deps\":[],\"files\":[]}]";
        var planner=new Model("glm-pm",r=> { Assert.True(r.ReadOnly); return Say(plan); },"opencode-go");
        var limited=new Model("limited",_=>[new UsageLimitHit("go","quota") { RetryAfter=TimeSpan.FromMinutes(9) }]);
        var healthy=new Model("healthy",r=>Say(r.ReadOnly?"PASS":"implemented"));
        var reviewer=new Model("reviewer",r=>Say(r.ReadOnly?"PASS":"implemented"));
        var runner=new TeamOrchestrator(()=>Loop(dir.Path),usage,new()) {FallbackPm=planner};
        var job=await runner.RunAsync("task",dir.Path,unavailable,[new(limited,["code"],["small"]),new(healthy,["code"],["small"])],true,"dotnet --version",default);
        Assert.Equal("glm-pm",job.Pm); Assert.Empty(healthy.Requests);
        var pm=new Model("opus",_=>Say(plan),"claude");
        runner=new TeamOrchestrator(()=>Loop(dir.Path),usage,new());
        await Assert.ThrowsAsync<InvalidOperationException>(()=>runner.RunAsync("task",dir.Path,pm,[new(limited,["*"],["*"]),new(healthy,["*"],["*"])],false,"dotnet --version",default));
        job=runner.Job;
        Assert.Contains(healthy.Requests,r=>!r.ReadOnly);
        Assert.NotNull(usage.Snapshot()[ModelUsageTracker.Key(limited)].BackoffUntil);
        Assert.Equal("failed",job.Status);
    }

    [Fact] public async Task AllDownReportsClearErrorAndNeverTriesFreeHttpModels()
    {
        var offline=new Model("glm",_=>[new ProviderUnavailable("go","offline")]); var free=new Model("zen-free",_=>Say("must not send"));
        var events=await new AutoChatProvider([offline,free],new()).StreamChatAsync(new([ChatMessage.User("hi")],[]),default).ToListAsync();
        Assert.Contains(events,e=>e is ProviderUnavailable {Message:"All Auto models unavailable or cooling down."}); Assert.Empty(free.Requests);
        Assert.False(new OrchDesk.Core.Config.AppConfig().PreferFreeModels);
    }

}
