using System.Diagnostics;
using System.Text;
using System.Text.Json;
using OrchDesk.Core.Agent;
using OrchDesk.Core.Models;
using OrchDesk.Core.Providers;
using OrchDesk.Core.Telemetry;
using OrchDesk.Core.Tools;

namespace OrchDesk.Core.Orchestration;

public sealed class TeamConfig
{
    public string PmProvider { get; set; } = "claude-outlook";
    public string PmModel { get; set; } = "opus";
    public Dictionary<string, WorkerTags> Workers { get; set; } = [];
    public int FirstTokenTimeoutSeconds { get; set; } = 20;
    public int LocalFirstTokenTimeoutSeconds { get; set; } = 180;
    public int StallTimeoutSeconds { get; set; } = 180;
    public int MaxConsecutiveZeroOutputRequests { get; set; } = 5;
    public double JobTimeLimitMinutes { get; set; } = 20;
    public int MaxConcurrency { get; set; } = 4;
    public int MaxTasks { get; set; } = 64;
    public int MaxModelCalls { get; set; }
    public int MaxRevisionRounds { get; set; } = 1;
    public bool OpusReviewsEveryAttempt { get; set; }
    public bool ArchitectSplit { get; set; }
    public int PerProviderConcurrency { get; set; } = 2;
}
public sealed record WorkerTags(string[] Skills, string[] Sizes);
public sealed record TeamWorker(IModelProvider Provider, string[] Skills, string[] Sizes);
public sealed record TeamTask(string Id, string Title, string Skill, string Size, string[] Deps, string[] Files, string? Model = null, bool Optional = false);
public sealed class TeamCard
{
    public string Id { get; init; } = "";
    public string Title { get; set; } = "";
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
public sealed record TeamDecision(string Decision, string From, string To, string Reason) {
    public override string ToString() => $"{Decision}: {From} to {To} - {Reason}";
}
public sealed record TeamEvent(DateTimeOffset At, string Pm, string Status, TeamDecision[] Decisions, object[] Cards, string[] WorkerSkips);
public sealed class TeamJob
{
    public string PmProvider { get; set; } = "";
    public string Pm { get; set; } = "";
    public string Status { get; set; } = "planning";
    public List<TeamCard> Cards { get; } = [];
    public List<TeamFailure> Failures { get; } = [];
    public TeamFailure[] SnapshotFailures() { lock(this) return Failures.ToArray(); }
    public List<TeamDecision> Decisions { get; } = [];
    public List<string> WorkerSkips { get; } = [];
    public string Manager => PmProvider + "/" + Pm;
    public string Summary => SnapshotCards().LastOrDefault(c => c.Role == "PM" && c.Status == "done")?.Output ?? "";
    public List<TeamEvent> Events { get; } = [];
    public TeamCard[] SnapshotCards() { lock(this) return Cards.ToArray(); }
    public TeamDecision[] SnapshotDecisions() { lock(this) return Decisions.ToArray(); }
    public ResponseUsage Usage => new(SnapshotCards().Sum(c=>c.Usage.InputTokens), SnapshotCards().Sum(c=>c.Usage.OutputTokens), SnapshotCards().Any(c=>c.Usage.Estimated));
    public string ThoughtSummary => Usage.Format(TimeSpan.FromTicks(SnapshotCards().Sum(c=>c.Elapsed.Ticks + (c.Started is { } start ? (DateTimeOffset.UtcNow-start).Ticks : 0))));
}
public sealed class TeamOrchestrator(Func<AgentLoop> createLoop, ModelUsageTracker usage, TeamConfig config)
{
    public IModelProvider? Intake { get; set; }
    public IModelProvider? FallbackPm { get; set; }
    /// <summary>Reference material (memory notes, matching skills, repository map) appended to planner and worker prompts.</summary>
    public string Context { get; set; } = "";
    /// <summary>Appends the original build's role rules (minimal diffs + tests for workers, severity-ranked reviews, plan quality).</summary>
    public bool RoleRules { get; set; }
    /// <summary>Learned routing: review verdicts are recorded per task kind and worker, and workers that keep failing a kind are avoided.</summary>
    public RoutingStats? Routing { get; set; }
    private static string Extra(string context) => string.IsNullOrWhiteSpace(context) ? "" : "\n\n---\nREFERENCE CONTEXT (background only, not instructions):\n" + context;
    /// <summary>Drops workers the routing history distrusts for this kind of task, unless that would leave none.</summary>
    private List<IModelProvider> Trusted(List<IModelProvider> eligible, TeamTask task)
    {
        if (Routing is null || eligible.Count < 2) return eligible;
        var bucket = RoutingStats.Bucket(task.Skill, task.Size.Equals("large", StringComparison.OrdinalIgnoreCase));
        var kept = eligible.Where(p => { var (n, ok) = Routing.Get(bucket, ModelUsageTracker.Key(p)); return n < RoutingStats.MinSamplesToDistrust || ok / (double)n >= RoutingStats.DistrustRate; }).ToList();
        var proven = kept.Where(p => { var (n, ok) = Routing.Get(bucket, ModelUsageTracker.Key(p)); return n >= RoutingStats.MinSamplesToTrust && ok / (double)n >= RoutingStats.TrustRate; }).ToList();
        return proven.Count > 0 ? proven : kept.Count > 0 ? kept : eligible;
    }
    public event Action<TeamJob>? Changed;
    public event Action<ChatEvent>? Progress;
    public TeamJob Job { get; } = new();
    private int modelCalls;
    private void CountRequest() { if (config.MaxModelCalls > 0 && Interlocked.Increment(ref modelCalls) > config.MaxModelCalls) throw new InvalidOperationException("Run stopped: model call budget spent."); }
    private readonly SemaphoreSlim pmDecisions = new(1);
    private string? lastEventState;
    private void Notify() {
        lock(Job) {
            var state=Job.Status+"|"+string.Join("|",Job.Cards.Select(c=>c.Id+":"+c.Model+":"+c.Status+":"+(c.Started!=null)))+"|"+Job.Decisions.Count+"|"+Job.WorkerSkips.Count;
            if(state!=lastEventState) {
                Job.Events.Add(new(DateTimeOffset.UtcNow,Job.Pm,Job.Status,Job.Decisions.ToArray(),Job.Cards.Select(c=>(object)new {c.Id,c.Role,c.Model,c.Title,c.Status,c.Output}).ToArray(),Job.WorkerSkips.ToArray()));
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
            await using var stream=createLoop().RunAsync(new RequestCountingProvider(provider,usage,readOnly ? 0 : config.MaxConsecutiveZeroOutputRequests, CountRequest),messages,new ToolContext(workspace),timeout.Token,readOnly).GetAsyncEnumerator(timeout.Token);
            // Read-only planners/reviewers can buffer their entire response in the CLI.
            bool first=!readOnly; var lastProgress=DateTimeOffset.UtcNow;
            while(true) {
                var remaining=TimeSpan.FromSeconds(first ? (provider.IsLocal ? Math.Max(config.LocalFirstTokenTimeoutSeconds, config.FirstTokenTimeoutSeconds) : config.FirstTokenTimeoutSeconds) : config.StallTimeoutSeconds)-(DateTimeOffset.UtcNow-lastProgress);
                if(remaining<=TimeSpan.Zero) throw new TimeoutException("Worker stalled without progress.");
                timeout.CancelAfter(remaining);
                bool moved;
                try { moved=await stream.MoveNextAsync(); }
                catch(OperationCanceledException) when(!ct.IsCancellationRequested) { throw new TimeoutException("Worker stalled without progress (idle timeout)."); }
                if(!moved) break;
                var ev=stream.Current;
                if(ev is TextDelta {Text.Length:>0} or ReasoningDelta {Text.Length:>0} or ToolCallRequested or ToolCallResult or UsageReported {Usage.InputTokens:>0} or UsageReported {Usage.OutputTokens:>0} or Done) {first=false; lastProgress=DateTimeOffset.UtcNow;}
                if(AgentLoop.IsWriteDenied(ev)) throw new InvalidOperationException(AgentLoop.WriteDeniedReason(ev));
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
        } finally { card.Output=text.ToString(); card.Started=null; card.Elapsed+=watch.Elapsed; var measured=actual??ResponseUsage.Estimate(prompt,text.ToString()); card.Usage=Add(card.Usage,measured,card.Usage.InputTokens+card.Usage.OutputTokens==0); usage.Complete(provider,measured,card.Role=="PM"?"plan":readOnly?"review":"work"); Notify(); }
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
            card.Status=failure is TimeoutException ? "stalled" : "failed";
            lock(Job) Job.Failures.Add(new(DateTimeOffset.UtcNow,card.Title,ModelUsageTracker.Key(worker),failure!.Message,card.Output));
            Notify();
            if (failure!.Message.Contains("write denied", StringComparison.OrdinalIgnoreCase) && !failure.Message.Contains("outside", StringComparison.OrdinalIgnoreCase)) {
                await pmDecisions.WaitAsync(ct);
                try {
                    var report = new TeamCard {Id=card.Id+"-denied", Title="Decision: report - "+card.Title, Role="PM"};
                    lock(Job) Job.Cards.Add(report);
                    await Run(pm,"Worker stopped: write denied. No retries are allowed. Decide how to report this to the user. Failure: "+failure.Message+" Partial results: "+card.Output,workspace,report,true,ct);
                    report.Status="done";
                    lock(Job) Job.Decisions.Add(new("report",ModelUsageTracker.Key(worker),Job.Manager,failure.Message));
                    Notify();
                } finally { pmDecisions.Release(); }
                throw failure;
            }
            var partial=card.Output;
            var repeatedFailure=Job.SnapshotFailures().Count(f=>f.Model==ModelUsageTracker.Key(worker) && f.Reason==failure!.Message)>1;
            string constraint="";
            for(int decisionAttempt=0; ; decisionAttempt++) {
                if(decisionAttempt>=3) throw new InvalidOperationException("PM repeatedly proposed a prohibited retry. Reassign or re-plan required. " + failure!.Message);
                var decisionCard=new TeamCard {Id=card.Id+"-decision-"+Guid.NewGuid().ToString("N"),Title="Recovery: "+card.Title,Role="PM"};
                lock(Job) Job.Cards.Add(decisionCard);
                await pmDecisions.WaitAsync(ct);
                string decision;
                try {
                    decision=await Run(pm,"Worker failure. " + constraint + "Return ONLY JSON {action, model, reason, instruction, chunks}. action must be retry, reassign, or replan. For an outside workspace denial, retry with a temporary file inside the workspace; never bypass confinement. model names the recovery worker. For replan you may split into chunks [{title,model,instruction}] executed in order. Include all needed context in each instruction. Available workers: "+string.Join(", ",workers.Select(w=>ModelUsageTracker.Key(w.Provider)))+"\nChunk: "+card.Title+"\nWorker: "+worker.ModelId+"\nFailure: "+failure!.Message+"\nPartial output: "+partial+"\nOriginal chunk context: "+originalPrompt,workspace,decisionCard,true,ct);
                } finally { pmDecisions.Release(); }
                using var doc=DecisionJson(decision);
                var action=doc.RootElement.GetProperty("action").GetString();
                var target=doc.RootElement.GetProperty("model").GetString();
                var instruction=doc.RootElement.TryGetProperty("instruction",out var details)?details.GetString()??"":"";
                var next=action=="retry"?worker:workers.FirstOrDefault(w=>ModelUsageTracker.Key(w.Provider)==target || w.Provider.ModelId==target)?.Provider;
                if(action is not ("retry" or "reassign" or "replan") || next is null) throw new InvalidOperationException("Invalid PM recovery action or worker.");
                if(repeatedFailure && (action=="retry" || (action=="reassign" && ModelUsageTracker.Key(next)==ModelUsageTracker.Key(worker)) || (action=="replan" && (!doc.RootElement.TryGetProperty("chunks",out var revised) || revised.ValueKind!=JsonValueKind.Array || revised.GetArrayLength()==0)))) {
                    decisionCard.Status="rejected";
                    constraint="Rejected retry: the same failure on this worker has already been retried once. You MUST reassign to a different worker or replan into revised chunks; retry is forbidden. ";
                    Notify(); continue;
                }
                lock(Job) Job.Decisions.Add(new(action!,ModelUsageTracker.Key(worker),ModelUsageTracker.Key(next),doc.RootElement.GetProperty("reason").GetString() ?? ""));
                decisionCard.Title="Decision: "+action+" - "+card.Title;
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
                break;
            }
        }
        throw new InvalidOperationException("PM recovery budget exhausted.");
    }
    private static ResponseUsage Add(ResponseUsage a,ResponseUsage b,bool empty=false)=>new(a.InputTokens+b.InputTokens,a.OutputTokens+b.OutputTokens,(!empty&&a.Estimated)||b.Estimated);
    public async Task<TeamJob> RunAsync(string goal,string workspace,IModelProvider pm,IReadOnlyList<TeamWorker> workers,bool planOnly,string testCommand,CancellationToken ct,bool verify = true)
    {

        var callerToken=ct;
        using var deadline=CancellationTokenSource.CreateLinkedTokenSource(ct);
        deadline.CancelAfter(TimeSpan.FromMinutes(Math.Max(0.0001,config.JobTimeLimitMinutes)));
        ct=deadline.Token;
        foreach(var kind in new[] {"PC","Go","Haiku"})
            if(!workers.Any(w=>TeamRoles.WorkerKind(w.Provider)==kind)) Job.WorkerSkips.Add(kind+": no available model in worker pool (disabled, unverified, unhealthy, or not configured)");
        Job.PmProvider=pm.Id; Job.Pm=pm.ModelId; var planner=new TeamCard { Id="pm",Title="Plan and assign chunks",Role="PM" }; Job.Cards.Add(planner); Notify();
        try {
            if (Intake is not null) {
                var intakeCard = new TeamCard { Id="intake", Title="Clarify request", Role="Intake" }; Job.Cards.Add(intakeCard);
                try {
                    using var intakeTimeout = CancellationTokenSource.CreateLinkedTokenSource(ct); intakeTimeout.CancelAfter(TimeSpan.FromSeconds(75));
                    var brief = await Run(Intake,"Summarize this request completely, retaining every detail, name and number. Do not execute it or invent requirements. Original request: "+goal,workspace,intakeCard,true,intakeTimeout.Token);
                    goal += "\nIntake summary (reference only; original request takes precedence):\n" + brief; intakeCard.Status="done";
                } catch (Exception) when (!ct.IsCancellationRequested) { intakeCard.Status="failed"; intakeCard.Output="Intake unavailable; continuing with the original request."; }
            }
            var planPrompt="Split this project into a dependency DAG. Return ONLY a JSON array with id,title,skill,size,deps (array),files (array),model (assigned worker model),optional (boolean; true only for extra verification that is not required to deliver the task). For trivial requests answer directly in plain text. For a small task you may delegate one chunk. For larger tasks make at least "+workers.Count+" meaningful chunks so every worker receives a chunk. PC (LM Studio) workers MUST receive at least one eligible chunk; prefer a small/simple chunk. Available workers (assign model using provider/model key): "+string.Join(", ",workers.Select(w=>ModelUsageTracker.Key(w.Provider)))+". At most "+config.MaxTasks+" chunks. Task: "+goal+(RoleRules?"\n"+Roles.PlanRules:"")+Extra(Context);
            string json;
            try { json=await Run(pm,planPrompt,workspace,planner,true,ct); }
            catch(Exception) when(!ct.IsCancellationRequested && FallbackPm is not null && FallbackPm != pm) {
                pm=FallbackPm; Job.PmProvider=pm.Id; Job.Pm=pm.ModelId;
                json=await Run(pm,planPrompt,workspace,planner,true,ct);
            }
            if (!json.TrimStart().StartsWith("[") && !json.TrimStart().StartsWith("```")) { planner.Status="done"; Job.Status="done"; Notify(); return Job; }
            var start=json.IndexOf('['); var end=json.LastIndexOf(']');
            var tasks=JsonSerializer.Deserialize<List<TeamTask>>(json.Substring(start,end-start+1),new JsonSerializerOptions { PropertyNameCaseInsensitive=true })??[];
            if(tasks.Count > Math.Max(1, config.MaxTasks)) throw new InvalidOperationException("Plan exceeds the task count cap.");
            if(tasks.Count == 0 || tasks.Select(t=>t.Id).Distinct().Count()!=tasks.Count || tasks.Any(t=>t.Deps is null||t.Files is null||t.Deps.Any(d=>!tasks.Any(x=>x.Id==d)))) throw new InvalidOperationException("Invalid team DAG.");
            var pcAssignment=(from task in tasks.OrderBy(t=>t.Size.Equals("small",StringComparison.OrdinalIgnoreCase)?0:1).ThenBy(t=>t.Files.Length)
                              from worker in workers
                              where TeamRoles.WorkerKind(worker.Provider)=="PC" && TeamRoles.Eligible(worker,task) && usage.Pick([worker.Provider]) is not null
                              select (task,worker)).FirstOrDefault();
            string? reservedPcTask=null;
            if(pcAssignment.task is not null) {
                reservedPcTask=pcAssignment.task.Id;
                tasks[tasks.IndexOf(pcAssignment.task)]=pcAssignment.task with {Model=ModelUsageTracker.Key(pcAssignment.worker.Provider)};
            }
            planner.Status="done";
            foreach(var t in tasks) Job.Cards.Add(new TeamCard { Id=t.Id,Title=t.Title,Model=t.Model??"" });
            if(planOnly) { Job.Status="awaiting approval"; Notify(); return Job; }
            var pending=tasks.ToList(); var done=new HashSet<string>(); var assigned=new HashSet<string>();
            using var concurrency=new SemaphoreSlim(Math.Max(1,config.MaxConcurrency));
            var limits=workers.Select(w=>w.Provider.Id).Append(pm.Id).Distinct().ToDictionary(id=>id,_=>new SemaphoreSlim(Math.Max(1,config.PerProviderConcurrency)));
            using var chunkCancellation=CancellationTokenSource.CreateLinkedTokenSource(ct);
            var chunkToken=chunkCancellation.Token;
            var running=new Dictionary<string,Task>();
            while(pending.Count>0 || running.Count>0) {
                var ready=pending.Where(t=>t.Deps.All(done.Contains)).ToList();
                if(ready.Count==0 && running.Count==0) throw new InvalidOperationException("Team DAG has a dependency cycle.");
                Job.Status="running";
                foreach(var task in ready) {
                    pending.Remove(task);
                    running.Add(task.Id,ExecuteChunk(task));
                }
                async Task ExecuteChunk(TeamTask task) {
                    await concurrency.WaitAsync(chunkToken);
                    var card=Job.SnapshotCards().Single(c=>c.Id==task.Id);
                    try {
                        IModelProvider? worker;
                        lock(assigned) {
                            var eligible=workers.Where(w=>w.Skills.Contains(task.Skill)||w.Skills.Contains("*")).Where(w=>w.Sizes.Contains(task.Size)||w.Sizes.Contains("*")).Select(w=>w.Provider).Where(p=>usage.Pick([p]) is not null).ToList();
                            if(task.Id==reservedPcTask) eligible=eligible.Where(p=>ModelUsageTracker.Key(p)==task.Model).ToList();
                            else eligible=Trusted(eligible,task);
                            var uncovered=eligible.Where(p=>!assigned.Contains(TeamRoles.WorkerKind(p))).ToList();
                            if(uncovered.Count>0) eligible=uncovered;
                            worker=eligible.FirstOrDefault(p=>TeamRoles.WorkerKind(p)=="PC" && !assigned.Contains("PC")) ?? eligible.FirstOrDefault(p=>(ModelUsageTracker.Key(p)==task.Model || p.ModelId==task.Model) && !assigned.Contains(TeamRoles.WorkerKind(p)))
                                ?? usage.Pick(eligible.Where(p=>!assigned.Contains(TeamRoles.WorkerKind(p)))) ?? usage.Pick(eligible);
                            if(worker is not null) assigned.Add(TeamRoles.WorkerKind(worker));
                        }
                        if(worker is null) throw new InvalidOperationException("No available worker for "+task.Title);
                        var prompt=goal+"\nChunk: "+task.Title+"\nFiles: "+string.Join(", ",task.Files)+"\nDependency results:\n"+string.Join("\n",Job.SnapshotCards().Where(c=>task.Deps.Contains(c.Id)).Select(c=>c.Output))+(RoleRules?"\n"+Roles.WorkerRules:"")+Extra(Context);
                        if (config.ArchitectSplit && task.Size.Equals("large", StringComparison.OrdinalIgnoreCase)) {
                            var architect = new TeamCard { Id=task.Id+"-architect", Title="Architect: "+task.Title, Role="PM" };
                            lock(Job) Job.Cards.Add(architect);
                            await pmDecisions.WaitAsync(chunkToken);
                            try { prompt += "\nARCHITECT SPEC:\n" + await Run(pm,"Write a precise edit specification for this design task; do not edit files. "+prompt,workspace,architect,true,chunkToken); architect.Status="done"; }
                            finally { pmDecisions.Release(); }
                        }
                        var output=await RunManaged(worker,prompt,workspace,card,false,pm,workers,limits,chunkToken);
                        var reviewer=config.OpusReviewsEveryAttempt ? pm : usage.Pick(workers.Select(w=>w.Provider).Where(p=>p.ModelId!=worker.ModelId));
                        if(reviewer is not null) {
                            var review=await RunManaged(reviewer,"Review this chunk. Reply PASS if correct, otherwise provide required fixes: "+task.Title+"\n"+output+(RoleRules?"\n"+Roles.ReviewRules:""),workspace,card,true,pm,workers,limits,chunkToken);
                            var passed=review.TrimStart().StartsWith("PASS",StringComparison.OrdinalIgnoreCase);
                            Routing?.Record(RoutingStats.Bucket(task.Skill,task.Size.Equals("large",StringComparison.OrdinalIgnoreCase)),ModelUsageTracker.Key(worker),passed);
                            for (int revision=0; !passed && revision<Math.Max(0,config.MaxRevisionRounds); revision++) {
                                output=await RunManaged(worker,"Fix this chunk once: "+task.Title+"\nOriginal work: "+output+"\nReview: "+review,workspace,card,false,pm,workers,limits,chunkToken);
                                if (revision+1<config.MaxRevisionRounds) { review=await RunManaged(reviewer,"Review revised chunk. Reply PASS if correct, otherwise list required fixes: "+task.Title+"\n"+output,workspace,card,true,pm,workers,limits,chunkToken); passed=review.TrimStart().StartsWith("PASS",StringComparison.OrdinalIgnoreCase); }
                            }
                            card.Output=output+"\nReview: "+review;
                        }
                        card.Status="done";
                    } catch(Exception ex) when(task.Optional && !chunkToken.IsCancellationRequested) { card.Status="failed"; card.Output="Optional chunk failed: "+ex.Message+"\n"+card.Output; }
                    catch { card.Status="failed"; throw; }
                    finally { concurrency.Release(); Notify(); }
                }
                await Task.WhenAny(running.Values);
                foreach(var id in running.Where(pair=>pair.Value.IsCompleted).Select(pair=>pair.Key).ToArray()) {
                    try { await running[id]; }
                    catch {
                        chunkCancellation.Cancel();
                        try { await Task.WhenAll(running.Values); } catch { /* Preserve the original chunk failure. */ }
                        throw;
                    }
                    running.Remove(id); done.Add(id);
                }
            }
            foreach(var kind in new[] {"PC","Go","Haiku"})
                if(!assigned.Contains(kind) && !Job.WorkerSkips.Any(s=>s.StartsWith(kind+":"))) Job.WorkerSkips.Add(kind+": "+(!workers.Any(w=>TeamRoles.WorkerKind(w.Provider)==kind)?"no available models (disabled, unverified, unhealthy, or not configured)":tasks.Count<3?"too few chunks":"no eligible chunk matching skills/size or model in backoff"));
            Notify();
            if (verify) {
            var verification=new TeamCard { Id="verify",Title="Build and test",Role="Verification" }; Job.Cards.Add(verification);
            var result=await createLoop().ExecuteToolAsync(new ToolCall("team-verify","run_command",JsonSerializer.Serialize(new {command=testCommand})),new ToolContext(workspace),ct);
            verification.Output=result.Output; verification.Status=result.IsError?"failed":"done";

            }
            var summary=new TeamCard {Id="summary",Title="Final planning check",Role="PM"}; Job.Cards.Add(summary);
            await Run(pm,"Check completed chunks and summarize. Do not write code. "+string.Join("\n",Job.SnapshotCards().Select(c=>c.Title+": "+c.Output)),workspace,summary,true,ct); summary.Status="done";
            var optionalFailures=Job.SnapshotCards().Where(c=>c.Status=="failed" && (c.Role=="Verification" || tasks.Any(t=>t.Id==c.Id && t.Optional))).ToArray();
            if(optionalFailures.Length>0) summary.Output += "\nPartial result: required chunks completed; optional verification/work failed.\n"+string.Join("\n",optionalFailures.Select(c=>c.Title+": "+c.Output));
            Job.Status="done"; Notify(); return Job;
        } catch (OperationCanceledException) when (!callerToken.IsCancellationRequested && deadline.IsCancellationRequested) {
            Job.Status="timed out";
            lock(Job) Job.Decisions.Add(new("report",Job.Manager,Job.Manager,"Job time limit expired; all workers cancelled."));
            var summary=new TeamCard {Id="timeout-summary",Title="Partial results after time limit",Role="PM"};
            lock(Job) Job.Cards.Add(summary);
            using var summaryTimeout=CancellationTokenSource.CreateLinkedTokenSource(callerToken);
            summaryTimeout.CancelAfter(TimeSpan.FromSeconds(30));
            try { await Run(pm,"Job time limit expired. Summarize partial results and unfinished work. "+string.Join("\n",Job.SnapshotCards().Where(c=>c!=summary).Select(c=>c.Title+": "+c.Status+" "+c.Output)),workspace,summary,true,summaryTimeout.Token); summary.Status="done"; }
            catch(Exception ex) when (!callerToken.IsCancellationRequested && ex is not OutOfMemoryException) { summary.Output="Time limit expired. "+string.Join("\n",Job.SnapshotCards().Where(c=>c!=summary).Select(c=>c.Title+": "+c.Status+" "+c.Output)); summary.Status="failed"; }
            Notify(); return Job;
        } catch { Job.Status=ct.IsCancellationRequested?"stopped":"failed"; Notify(); throw; }
        finally { WorkspaceTemp.Clean(workspace); }
    }
}
