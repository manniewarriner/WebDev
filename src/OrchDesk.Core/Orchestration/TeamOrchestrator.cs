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
    public int FirstTokenTimeoutSeconds { get; set; } = 20;
    public int StallTimeoutSeconds { get; set; } = 300;
    public int MaxConcurrency { get; set; } = 4;
    public int PerProviderConcurrency { get; set; } = 2;
}
public sealed record WorkerTags(string[] Skills, string[] Sizes);
public sealed record TeamWorker(IModelProvider Provider, string[] Skills, string[] Sizes);
public sealed record TeamTask(string Id, string Title, string Skill, string Size, string[] Deps, string[] Files, string? Model = null);
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
public sealed record TeamFailure(DateTimeOffset At, string Chunk, string Model, string Reason, string PartialOutput);
public sealed record TeamEvent(DateTimeOffset At, string Pm, string Status, string[] Decisions, object[] Cards);
public sealed class TeamJob
{
    public string PmProvider { get; set; } = "";
    public string Pm { get; set; } = "";
    public string Status { get; set; } = "planning";
    public List<TeamCard> Cards { get; } = [];
    public List<TeamFailure> Failures { get; } = [];
    public TeamFailure[] SnapshotFailures() { lock(this) return Failures.ToArray(); }
    public List<string> Decisions { get; } = [];
    public List<TeamEvent> Events { get; } = [];
    public TeamCard[] SnapshotCards() { lock(this) return Cards.ToArray(); }
    public string[] SnapshotDecisions() { lock(this) return Decisions.ToArray(); }
    public ResponseUsage Usage => new(SnapshotCards().Sum(c=>c.Usage.InputTokens), SnapshotCards().Sum(c=>c.Usage.OutputTokens), SnapshotCards().Any(c=>c.Usage.Estimated));
    public string ThoughtSummary => Usage.Format(TimeSpan.FromTicks(SnapshotCards().Sum(c=>c.Elapsed.Ticks + (c.Started is { } start ? (DateTimeOffset.UtcNow-start).Ticks : 0))));
}
public sealed class TeamOrchestrator(Func<AgentLoop> createLoop, ModelUsageTracker usage, TeamConfig config)
{
    public IModelProvider? FallbackPm { get; set; }
    public event Action<TeamJob>? Changed;
    public event Action<ChatEvent>? Progress;
    public TeamJob Job { get; } = new();
    private readonly SemaphoreSlim pmDecisions = new(1);
    private string? lastEventState;
    private void Notify() {
        lock(Job) {
            var state=Job.Status+"|"+string.Join("|",Job.Cards.Select(c=>c.Id+":"+c.Model+":"+c.Status+":"+(c.Started!=null)))+"|"+Job.Decisions.Count;
            if(state!=lastEventState) {
                Job.Events.Add(new(DateTimeOffset.UtcNow,Job.Pm,Job.Status,Job.Decisions.ToArray(),Job.Cards.Select(c=>(object)new {c.Id,c.Role,c.Model,c.Title,c.Status,c.Output}).ToArray()));
                lastEventState=state;
            }
        }
        Changed?.Invoke(Job);
    }
    private static JsonDocument DecisionJson(string json) {
        try { var start=json.IndexOf('{'); var end=json.LastIndexOf('}'); return JsonDocument.Parse(json.Substring(start,end-start+1)); }
        catch(Exception ex) when(ex is JsonException or ArgumentException) {throw new InvalidOperationException("PM returned an invalid recovery decision.",ex);}
    }
    private async Task<string> Run(IModelProvider provider, string prompt, string workspace, TeamCard card, bool readOnly, CancellationToken ct)
    {
        card.Started=DateTimeOffset.UtcNow; card.Model = provider.ModelId; card.Status = readOnly ? "review" : "running"; Notify();
        var text = new StringBuilder(); var watch=Stopwatch.StartNew(); ResponseUsage? actual=null; bool completedTool=false;
        var messages = new List<ChatMessage> { ChatMessage.System(readOnly ? "You only plan or review. Never write code, edit files or execute commands." : AgentLoop.DefaultSystemPrompt), ChatMessage.User(prompt) };
        try {
            using var timeout=CancellationTokenSource.CreateLinkedTokenSource(ct);
            await using var stream=createLoop().RunAsync(new RequestCountingProvider(provider,usage),messages,new ToolContext(workspace),timeout.Token,readOnly).GetAsyncEnumerator(timeout.Token);
            bool first=true; var lastProgress=DateTimeOffset.UtcNow;
            while(true) {
                var remaining=TimeSpan.FromSeconds(first ? config.FirstTokenTimeoutSeconds : config.StallTimeoutSeconds)-(DateTimeOffset.UtcNow-lastProgress);
                if(remaining<=TimeSpan.Zero) throw new TimeoutException("Worker stalled without progress.");
                timeout.CancelAfter(remaining);
                if(!await stream.MoveNextAsync()) break;
                var ev=stream.Current;
                if(ev is TextDelta or ReasoningDelta or ToolCallRequested or ToolCallResult or Done) {first=false; lastProgress=DateTimeOffset.UtcNow;}
                if(ev is ToolCallResult {IsError:false}) completedTool=true;
                if(ev is Done {FinalText.Length:>0} final && text.Length==0) text.Append(final.FinalText);
                if(ev is UsageReported u) actual=actual is null ? u.Usage : Add(actual,u.Usage);
                if(ev is TextDelta d) { text.Append(d.Text); card.Output=text.ToString(); Notify(); }
                if(ev is UsageLimitHit || ev.HttpStatus == 429) { usage.Backoff(provider,ev.RetryAfter); card.Status="backoff"; Notify(); throw new InvalidOperationException("Model usage limit; reassignment required."); }
                if(ev is ErrorEvent or ProviderUnavailable) throw new InvalidOperationException("Model unavailable: " + provider.Id + " - " + (ev is ProviderUnavailable unavailable ? unavailable.Message : ev is ErrorEvent error ? error.Message : "unknown"));
                if(ev is ToolCallRequested or ToolCallResult) Progress?.Invoke(ev);
            }
            if(text.Length==0 && !completedTool) throw new InvalidOperationException("Provider ended without an answer or completed work.");
            card.Output=text.ToString(); return card.Output;
        } finally { card.Output=text.ToString(); card.Started=null; card.Elapsed+=watch.Elapsed; var measured=actual??ResponseUsage.Estimate(prompt,text.ToString()); card.Usage=Add(card.Usage,measured,card.Usage.InputTokens+card.Usage.OutputTokens==0); usage.Complete(provider,measured); Notify(); }
    }
    private async Task<string> RunManaged(IModelProvider worker,string prompt,string workspace,TeamCard card,bool readOnly,IModelProvider pm,IReadOnlyList<TeamWorker> workers,Dictionary<string,SemaphoreSlim> limits,CancellationToken ct,int depth=0)
    {
        if(depth>3) throw new InvalidOperationException("PM re-plan depth exhausted.");
        var originalPrompt=prompt;
        for(int attempt=1;attempt<=6;attempt++) {
            Exception? failure=null;
            await limits[worker.Id].WaitAsync(ct);
            try { return await Run(worker,prompt,workspace,card,readOnly,ct); }
            catch(Exception ex) when(!ct.IsCancellationRequested && ex is not OutOfMemoryException) { failure=ex; }
            finally { limits[worker.Id].Release(); }
            card.Status="failed";
            lock(Job) Job.Failures.Add(new(DateTimeOffset.UtcNow,card.Title,ModelUsageTracker.Key(worker),failure!.Message,card.Output));
            Notify();
            var partial=card.Output;
            var decisionCard=new TeamCard {Id=card.Id+"-decision-"+Guid.NewGuid().ToString("N"),Title="Recovery: "+card.Title,Role="PM"};
            lock(Job) Job.Cards.Add(decisionCard);
            await pmDecisions.WaitAsync(ct);
            string decision;
            try {
                decision=await Run(pm,"Worker failure. Return ONLY JSON {action, model, reason, instruction, chunks}. action must be retry, reassign, or replan. model names the recovery worker. For replan you may split into chunks [{title,model,instruction}] executed in order. Include all needed context in each instruction. Available workers: "+string.Join(", ",workers.Select(w=>ModelUsageTracker.Key(w.Provider)))+"\nChunk: "+card.Title+"\nWorker: "+worker.ModelId+"\nFailure: "+failure!.Message+"\nPartial output: "+partial+"\nOriginal chunk context: "+originalPrompt,workspace,decisionCard,true,ct);
            } finally { pmDecisions.Release(); }
            using var doc=DecisionJson(decision);
            var action=doc.RootElement.GetProperty("action").GetString();
            var target=doc.RootElement.GetProperty("model").GetString();
            var instruction=doc.RootElement.TryGetProperty("instruction",out var details)?details.GetString()??"":"";
            var next=action=="retry"?worker:workers.FirstOrDefault(w=>ModelUsageTracker.Key(w.Provider)==target || w.Provider.ModelId==target)?.Provider;
            if(action is not ("retry" or "reassign" or "replan") || next is null) throw new InvalidOperationException("Invalid PM recovery action or worker.");
            var verb=action=="reassign"?"reassigned":action=="retry"?"retry":"replanned";
            lock(Job) Job.Decisions.Add($"{pm.ModelId}: {verb} '{card.Title}' from {worker.ModelId} to {next.ModelId} - {doc.RootElement.GetProperty("reason").GetString()}");
            decisionCard.Status="done"; Notify();
            worker=next;
            if(action=="replan" && doc.RootElement.TryGetProperty("chunks",out var chunks) && chunks.ValueKind==JsonValueKind.Array && chunks.GetArrayLength()>0) {
                var results=new List<string>();
                foreach(var chunk in chunks.EnumerateArray()) {
                    var model=chunk.GetProperty("model").GetString();
                    var assigned=workers.FirstOrDefault(w=>ModelUsageTracker.Key(w.Provider)==model || w.Provider.ModelId==model)?.Provider ?? throw new InvalidOperationException("PM re-plan names an unavailable worker.");
                    var subcard=new TeamCard {Id=card.Id+"-split-"+Guid.NewGuid().ToString("N"),Title=chunk.GetProperty("title").GetString()??card.Title};
                    lock(Job) Job.Cards.Add(subcard);
                    var result=await RunManaged(assigned,"PM revised chunk: "+chunk.GetProperty("instruction").GetString()+"\nPrevious split results: "+string.Join("\n",results),workspace,subcard,readOnly,pm,workers,limits,ct,depth+1);
                    subcard.Status="done"; results.Add(result); Notify();
                }
                card.Output=string.Join("\n",results); return card.Output;
            }
            prompt=originalPrompt+"\nPM recovery instruction: "+instruction+"\nPartial output from previous worker: "+partial;
            card.Status="queued"; Notify();
        }
        throw new InvalidOperationException("PM recovery budget exhausted.");
    }
    private static ResponseUsage Add(ResponseUsage a,ResponseUsage b,bool empty=false)=>new(a.InputTokens+b.InputTokens,a.OutputTokens+b.OutputTokens,(!empty&&a.Estimated)||b.Estimated);
    public async Task<TeamJob> RunAsync(string goal,string workspace,IModelProvider pm,IReadOnlyList<TeamWorker> workers,bool planOnly,string testCommand,CancellationToken ct,bool verify = true)
    {

        Job.PmProvider=pm.Id; Job.Pm=pm.ModelId; var planner=new TeamCard { Id="pm",Title="Plan and assign chunks",Role="PM" }; Job.Cards.Add(planner); Notify();
        try {
            var planPrompt="Split this project into a dependency DAG. Return ONLY a JSON array with id,title,skill,size,deps (array),files (array),model (assigned worker model). For trivial requests answer directly in plain text. For a small task you may delegate one chunk. For larger tasks make at least "+workers.Count+" meaningful chunks so every worker receives a chunk. Available workers (assign model using provider/model key): "+string.Join(", ",workers.Select(w=>ModelUsageTracker.Key(w.Provider)))+". Task: "+goal;
            string json;
            try { json=await Run(pm,planPrompt,workspace,planner,true,ct); }
            catch(Exception) when(!ct.IsCancellationRequested && FallbackPm is not null && FallbackPm != pm) {
                pm=FallbackPm; Job.PmProvider=pm.Id; Job.Pm=pm.ModelId;
                json=await Run(pm,planPrompt,workspace,planner,true,ct);
            }
            if (!json.TrimStart().StartsWith("[") && !json.TrimStart().StartsWith("```")) { planner.Status="done"; Job.Status="done"; Notify(); return Job; }
            var start=json.IndexOf('['); var end=json.LastIndexOf(']');
            var tasks=JsonSerializer.Deserialize<List<TeamTask>>(json.Substring(start,end-start+1),new JsonSerializerOptions { PropertyNameCaseInsensitive=true })??[];
            if(tasks.Count == 0 || tasks.Select(t=>t.Id).Distinct().Count()!=tasks.Count || tasks.Any(t=>t.Deps is null||t.Files is null||t.Deps.Any(d=>!tasks.Any(x=>x.Id==d)))) throw new InvalidOperationException("Invalid team DAG.");
            planner.Status="done";
            foreach(var t in tasks) Job.Cards.Add(new TeamCard { Id=t.Id,Title=t.Title,Model=t.Model??"" });
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
                    var card=Job.SnapshotCards().Single(c=>c.Id==task.Id);
                    try {
                        IModelProvider? worker;
                        lock(assigned) {
                            var eligible=workers.Where(w=>w.Skills.Contains(task.Skill)||w.Skills.Contains("*")).Where(w=>w.Sizes.Contains(task.Size)||w.Sizes.Contains("*")).Select(w=>w.Provider).ToList();
                            worker=workers.FirstOrDefault(w=>ModelUsageTracker.Key(w.Provider)==task.Model || w.Provider.ModelId==task.Model)?.Provider
                                ?? usage.Pick(eligible.Where(p=>!assigned.Contains(ModelUsageTracker.Key(p)))) ?? usage.Pick(eligible);
                            if(worker is not null) assigned.Add(ModelUsageTracker.Key(worker));
                        }
                        if(worker is null) throw new InvalidOperationException("No available worker for "+task.Title);
                        var prompt=goal+"\nChunk: "+task.Title+"\nFiles: "+string.Join(", ",task.Files)+"\nDependency results:\n"+string.Join("\n",Job.SnapshotCards().Where(c=>task.Deps.Contains(c.Id)).Select(c=>c.Output));
                        var output=await RunManaged(worker,prompt,workspace,card,false,pm,workers,limits,ct);
                        var reviewer=usage.Pick(workers.Select(w=>w.Provider).Where(p=>p.ModelId!=worker.ModelId));
                        if(reviewer is not null) {
                            var review=await RunManaged(reviewer,"Review this chunk. Reply PASS if correct, otherwise provide required fixes: "+task.Title+"\n"+output,workspace,card,true,pm,workers,limits,ct);
                            if(!review.TrimStart().StartsWith("PASS",StringComparison.OrdinalIgnoreCase))
                                output=await RunManaged(worker,"Fix this chunk once: "+task.Title+"\nOriginal work: "+output+"\nReview: "+review,workspace,card,false,pm,workers,limits,ct);
                            card.Output=output+"\nReview: "+review;
                        }
                        card.Status="done";
                    } catch { card.Status="failed"; throw; }
                    finally { concurrency.Release(); Notify(); }
                }));
                foreach(var task in ready) { pending.Remove(task); done.Add(task.Id); }
            }
            if (verify) {
            var verification=new TeamCard { Id="verify",Title="Build and test",Role="Verification" }; Job.Cards.Add(verification);
            var result=await createLoop().ExecuteToolAsync(new ToolCall("team-verify","run_command",JsonSerializer.Serialize(new {command=testCommand})),new ToolContext(workspace),ct);
            verification.Output=result.Output; verification.Status=result.IsError?"failed":"done";
            if(result.IsError) throw new InvalidOperationException("Team verification failed.");
            }
            var summary=new TeamCard {Id="summary",Title="Final planning check",Role="PM"}; Job.Cards.Add(summary);
            await Run(pm,"Check completed chunks and summarize. Do not write code. "+string.Join("\n",Job.SnapshotCards().Select(c=>c.Title+": "+c.Output)),workspace,summary,true,ct); summary.Status="done";
            Job.Status="done"; Notify(); return Job;
        } catch { Job.Status=ct.IsCancellationRequested?"stopped":"failed"; Notify(); throw; }
    }
}
