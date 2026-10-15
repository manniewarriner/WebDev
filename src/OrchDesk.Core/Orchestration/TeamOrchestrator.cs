using System.Diagnostics;
using System.Text;
using System.Text.Json;
using OrchDesk.Core.Agent;
using OrchDesk.Core.Models;
using OrchDesk.Core.Providers;
using OrchDesk.Core.Tools;

namespace OrchDesk.Core.Orchestration;

public sealed class TeamConfig
{
    public string PmProvider { get; set; } = "claude-outlook";
    public string PmModel { get; set; } = "opus";
    public Dictionary<string, WorkerTags> Workers { get; set; } = [];
    public int MaxConcurrency { get; set; } = 4;
    public int PerProviderConcurrency { get; set; } = 2;
}
public sealed record WorkerTags(string[] Skills, string[] Sizes);
public sealed record TeamWorker(IModelProvider Provider, string[] Skills, string[] Sizes);
public sealed record TeamTask(string Id, string Title, string Skill, string Size, string[] Deps, string[] Files);
public sealed class TeamCard
{
    public string Id { get; init; } = "";
    public string Title { get; init; } = "";
    public string Role { get; set; } = "Worker";
    public string Model { get; set; } = "";
    public string Status { get; set; } = "queued";
    public string Output { get; set; } = "";
    public TimeSpan Elapsed { get; set; }
    public ResponseUsage Usage { get; set; } = new(0,0,true);
    public DateTimeOffset? Started { get; set; }
    public string ThoughtSummary => Usage.Format(Elapsed + (Started is { } start ? DateTimeOffset.UtcNow - start : TimeSpan.Zero));
    public string Tooltip => Usage.Tooltip(Role, Model);
}
public sealed class TeamJob
{
    public string Pm { get; set; } = "";
    public string Status { get; set; } = "planning";
    public List<TeamCard> Cards { get; } = [];
    public ResponseUsage Usage => new(Cards.Sum(c=>c.Usage.InputTokens), Cards.Sum(c=>c.Usage.OutputTokens), Cards.Any(c=>c.Usage.Estimated));
    public string ThoughtSummary => Usage.Format(TimeSpan.FromTicks(Cards.Sum(c=>c.Elapsed.Ticks + (c.Started is { } start ? (DateTimeOffset.UtcNow-start).Ticks : 0))));
}
public sealed class TeamOrchestrator(Func<AgentLoop> createLoop, ModelUsageTracker usage, TeamConfig config)
{
    public IModelProvider? FallbackPm { get; set; }
    public event Action<TeamJob>? Changed;
    public event Action<ChatEvent>? Progress;
    public TeamJob Job { get; } = new();
    private void Notify() { lock(Job) Changed?.Invoke(Job); }
    private async Task<string> Run(IModelProvider provider, string prompt, string workspace, TeamCard card, bool readOnly, CancellationToken ct)
    {
        card.Started=DateTimeOffset.UtcNow; card.Model = provider.ModelId; card.Status = readOnly ? "review" : "running"; Notify();
        var text = new StringBuilder(); var watch=Stopwatch.StartNew(); ResponseUsage? actual=null;
        var messages = new List<ChatMessage> { ChatMessage.System(readOnly ? "You only plan or review. Never write code, edit files or execute commands." : AgentLoop.DefaultSystemPrompt), ChatMessage.User(prompt) };
        try {
            using var timeout=CancellationTokenSource.CreateLinkedTokenSource(ct);
            await using var stream=createLoop().RunAsync(new RequestCountingProvider(provider,usage),messages,new ToolContext(workspace),timeout.Token,readOnly).GetAsyncEnumerator(timeout.Token);
            bool first=true;
            while(true) {
                timeout.CancelAfter(first ? TimeSpan.FromSeconds(20) : TimeSpan.FromMinutes(5));
                if(!await stream.MoveNextAsync()) break;
                first=false; var ev=stream.Current;
                if(ev is UsageReported u) actual=actual is null ? u.Usage : Add(actual,u.Usage);
                if(ev is TextDelta d) text.Append(d.Text);
                if(ev is UsageLimitHit || ev.HttpStatus == 429) { usage.Backoff(provider,ev.RetryAfter); throw new InvalidOperationException("Model usage limit; reassignment required."); }
                if(ev is ErrorEvent or ProviderUnavailable) throw new InvalidOperationException("Model unavailable: " + provider.Id);
                if(ev is ToolCallRequested or ToolCallResult) Progress?.Invoke(ev);
            }
            card.Output=text.ToString(); return card.Output;
        } finally { card.Started=null; card.Elapsed+=watch.Elapsed; var measured=actual??ResponseUsage.Estimate(prompt,text.ToString()); card.Usage=Add(card.Usage,measured,card.Usage.InputTokens+card.Usage.OutputTokens==0); usage.Complete(provider,measured); Notify(); }
    }
    private static ResponseUsage Add(ResponseUsage a,ResponseUsage b,bool empty=false)=>new(a.InputTokens+b.InputTokens,a.OutputTokens+b.OutputTokens,(!empty&&a.Estimated)||b.Estimated);
    public async Task<TeamJob> RunAsync(string goal,string workspace,IModelProvider pm,IReadOnlyList<TeamWorker> workers,bool planOnly,string testCommand,CancellationToken ct)
    {
        if (workers.Count == 0) throw new InvalidOperationException("Team unavailable: no workers for PM " + pm.Id + "/" + pm.ModelId);
        Job.Pm=pm.ModelId; var planner=new TeamCard { Id="pm",Title="Plan and assign chunks",Role="PM" }; Job.Cards.Add(planner); Notify();
        try {
            var planPrompt="Split this project into a dependency DAG. Return ONLY a JSON array with id,title,skill,size,deps (array),files (array). Make at least "+workers.Count+" meaningful chunks so every worker receives a chunk. Available workers: "+string.Join(", ",workers.Select(w=>w.Provider.ModelId))+". Task: "+goal;
            string json;
            try { json=await Run(pm,planPrompt,workspace,planner,true,ct); }
            catch(Exception) when(!ct.IsCancellationRequested && FallbackPm is not null && FallbackPm != pm) {
                pm=FallbackPm; Job.Pm=pm.ModelId;
                json=await Run(pm,planPrompt,workspace,planner,true,ct);
            }
            var start=json.IndexOf('['); var end=json.LastIndexOf(']');
            var tasks=JsonSerializer.Deserialize<List<TeamTask>>(json.Substring(start,end-start+1),new JsonSerializerOptions { PropertyNameCaseInsensitive=true })??[];
            if(tasks.Count < workers.Count || tasks.Select(t=>t.Id).Distinct().Count()!=tasks.Count || tasks.Any(t=>t.Deps is null||t.Files is null||t.Deps.Any(d=>!tasks.Any(x=>x.Id==d)))) throw new InvalidOperationException("Invalid team DAG.");
            planner.Status="done";
            foreach(var t in tasks) Job.Cards.Add(new TeamCard { Id=t.Id,Title=t.Title });
            if(planOnly) { Job.Status="awaiting approval"; Notify(); return Job; }
            var pending=tasks.ToList(); var done=new HashSet<string>(); var assigned=new HashSet<string>();
            using var concurrency=new SemaphoreSlim(Math.Max(1,config.MaxConcurrency));
            var limits=workers.Select(w=>w.Provider.Id).Distinct().ToDictionary(id=>id,_=>new SemaphoreSlim(Math.Max(1,config.PerProviderConcurrency)));
            while(pending.Count>0) {
                var ready=pending.Where(t=>t.Deps.All(done.Contains)).ToList();
                if(ready.Count==0) throw new InvalidOperationException("Team DAG has a dependency cycle.");
                Job.Status="running";
                await Task.WhenAll(ready.Select(async task=> {
                    await concurrency.WaitAsync(ct);
                    var card=Job.Cards.Single(c=>c.Id==task.Id);
                    try {
                        var tried=new HashSet<string>(); IModelProvider? worker=null;
                        while(true) {
                            lock(assigned) {
                                var eligible=workers.Where(w=>w.Skills.Contains(task.Skill)||w.Skills.Contains("*")).Where(w=>w.Sizes.Contains(task.Size)||w.Sizes.Contains("*")).Select(w=>w.Provider).ToList();
                                worker=usage.Pick(eligible.Where(p=>!assigned.Contains(ModelUsageTracker.Key(p))),tried)??usage.Pick(eligible,tried);
                                if(worker is not null) { assigned.Add(ModelUsageTracker.Key(worker)); tried.Add(ModelUsageTracker.Key(worker)); }
                            }
                            if(worker is null) throw new InvalidOperationException("No available worker for "+task.Title);
                            await limits[worker.Id].WaitAsync(ct);
                            try { await Run(worker,goal+"\nChunk: "+task.Title+"\nFiles: "+string.Join(", ",task.Files)+"\nDependency results:\n"+string.Join("\n",Job.Cards.Where(c=>task.Deps.Contains(c.Id)).Select(c=>c.Output)),workspace,card,false,ct); break; }
                            catch(Exception ex) when(!ct.IsCancellationRequested && ex is InvalidOperationException or OperationCanceledException or HttpRequestException) { card.Status="queued"; }
                            finally { limits[worker.Id].Release(); }
                        }
                        var reviewer=usage.Pick(workers.Select(w=>w.Provider).Where(p=>p.ModelId!=worker.ModelId));
                        if(reviewer is null) throw new InvalidOperationException("A different review model is unavailable.");
                        var output=card.Output;
                        await limits[reviewer.Id].WaitAsync(ct);
                        string review;
                        try { review=await Run(reviewer,"Review this chunk. Reply PASS if correct, otherwise provide required fixes: "+task.Title+"\n"+output,workspace,card,true,ct); } finally { limits[reviewer.Id].Release(); }
                        if(!review.TrimStart().StartsWith("PASS",StringComparison.OrdinalIgnoreCase)) await Run(worker,"Fix this chunk once: "+task.Title+"\n"+review,workspace,card,false,ct);
                        card.Output=(card.Output.Length>0?card.Output:output)+"\nReview: "+review; card.Status="done";
                    } catch { card.Status="failed"; throw; }
                    finally { concurrency.Release(); Notify(); }
                }));
                foreach(var task in ready) { pending.Remove(task); done.Add(task.Id); }
            }
            var verification=new TeamCard { Id="verify",Title="Build and test",Role="Verification" }; Job.Cards.Add(verification);
            var result=await createLoop().ExecuteToolAsync(new ToolCall("team-verify","run_command",JsonSerializer.Serialize(new {command=testCommand})),new ToolContext(workspace),ct);
            verification.Output=result.Output; verification.Status=result.IsError?"failed":"done";
            if(result.IsError) throw new InvalidOperationException("Team verification failed.");
            var summary=new TeamCard {Id="summary",Title="Final planning check",Role="PM"}; Job.Cards.Add(summary);
            await Run(pm,"Check completed chunks and summarize. Do not write code. "+string.Join("\n",Job.Cards.Select(c=>c.Title+": "+c.Output)),workspace,summary,true,ct); summary.Status="done";
            Job.Status="done"; Notify(); return Job;
        } catch { Job.Status=ct.IsCancellationRequested?"stopped":"failed"; Notify(); throw; }
    }
}
