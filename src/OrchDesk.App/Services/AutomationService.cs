using System.IO;
using System.Net.Http;
using System.Text;
using System.Windows.Threading;
using OrchDesk.Core.Automation;
using OrchDesk.Core.Config;
using OrchDesk.Core.Dashboard;
using OrchDesk.Core.Knowledge;
using OrchDesk.Core.Models;
using OrchDesk.Core.Orchestration;
using OrchDesk.Core.Permissions;

namespace OrchDesk.App.Services;

/// <summary>Owns background work for one app lifetime. Never starts in fixture or test mode.</summary>
public sealed class AutomationService : IRemoteHost, IJobNotificationHost, IDisposable
{
    private readonly AppServices services;
    private readonly Func<bool> interactiveBusy;
    private readonly HttpClient http = new() { Timeout = TimeSpan.FromSeconds(40) };
    private readonly RemoteApprovals approvals = new();
    private readonly DispatcherTimer timer = new() { Interval = TimeSpan.FromSeconds(30) };
    private CancellationTokenSource? poll, run;
    private int busy;
    public bool Busy => Volatile.Read(ref busy) != 0 || interactiveBusy();
    public string LastResult { get; private set; } = "Ready";
    public ChangeLog Changes { get; } = new();
    public SupervisorQueue Supervisor { get; }
    public TelegramChannel? Telegram { get; private set; }
    public LocalApiHost Api { get; }
    public AutomationService(AppServices services, Func<bool>? interactiveBusy = null)
    {
        this.services = services;
        this.interactiveBusy = interactiveBusy ?? (() => false);
        services.AutomationBusy = () => Volatile.Read(ref busy) != 0;
        Supervisor = new(services.Config, async (task, ct) => {
            task.Reason = await RunGoalAsync(task.Goal, task.Project, false, false, ct, task.Model);
        }, statePath: Path.Combine(services.Store.Directory, "supervisor-state.json"));
        Api = new(new DashboardApi(services.Tasks, services.Reports, () => services.SecretFile.Get("dashboard-api")));
        timer.Tick += async (_, _) => await TickAsync();
        services.Rebuilt += RestartTelegram;
    }
    public void Start() { timer.Start(); RestartTelegram(); }
    public void RestartTelegram()
    {
        poll?.Cancel(); poll?.Dispose(); poll = null; Telegram = null;
        if (!services.Config.TelegramEnabled || !services.SecretFile.Has("telegram")) return;
        Telegram = new(http, services.SecretFile.Get("telegram")!, services.Config, this, _ => services.Store.Save(services.Config), services.SecretFile.Redact);
        poll = new(); _ = Telegram.RunAsync(poll.Token);
    }
    private async Task TickAsync()
    {
        if (Busy) return;
        foreach (var job in services.Schedules.Due(DateTime.Now))
        {
            if (Busy) break;
            services.Schedules.MarkRan(job.Id, DateTime.Now);
            using var ct = new CancellationTokenSource(TimeSpan.FromMinutes(Math.Clamp(job.MaxMinutes, 1, 120)));
            try { await RunGoalAsync(job.Prompt, job.Project, job.Mode == ScheduleMode.Plan, false, ct.Token); services.Schedules.RecordResult(job.Id, true); }
            catch (Exception e) { LastResult = services.SecretFile.Redact(e.Message); services.Schedules.RecordResult(job.Id, false); }
        }
    }
    public string StatusText()
    {
        var state = services.LiveStatus ?? StatusMessageBuilder.FromFiles(services.Store.Directory);
        var task = Supervisor.Tasks.LastOrDefault(t => t.Status is QueueTaskState.Running or QueueTaskState.WaitingReset);
        return StatusMessageBuilder.Build(state with { Busy = Busy, Loop = task == null && state.Loop != "Idle" ? state.Loop : $"{Supervisor.State}, round {task?.Runs ?? 0}, next run: {task?.ResetAt?.ToString("O") ?? "unscheduled"}" }, DateTimeOffset.UtcNow, services.SecretFile.Redact);
    }
    public Task<string> RunAsync(string text, bool planOnly, CancellationToken ct) => RunGoalAsync(text, services.Config.LastWorkspace ?? services.Config.Project, planOnly, true, ct);
    public async Task<string> RunGoalAsync(string goal, string project, bool planOnly, bool remote, CancellationToken ct, string? preferredProvider = null)
    {
        if (!Directory.Exists(project)) throw new ArgumentException("Choose an existing project folder in OrchDesk first.");
        if (interactiveBusy()) throw new InvalidOperationException("The desktop chat is busy.");
        if (Interlocked.CompareExchange(ref busy, 1, 0) != 0) throw new InvalidOperationException("Another automation run is active.");
        run = CancellationTokenSource.CreateLinkedTokenSource(ct);
        run.CancelAfter(TimeSpan.FromMinutes(Math.Clamp(services.Config.SupervisorMaxMinutes, 1, 1440)));
        var started = DateTimeOffset.UtcNow;
        var jobId = Guid.NewGuid().ToString(); var result = "Done"; var summary = "";
        OrchDesk.Core.Telemetry.RunLog? log = null;
        services.LiveStatus = new(true, goal, project, started);
        try
        {
            log = new(Path.Combine(services.Store.Directory, "runs"), services.SecretFile.Redact, keep: int.MaxValue);
            log.Write("request", goal, title: goal, project: project);
            if (services.Config.DailyTokenCeiling > 0 && services.UsageLog.Summarize(1).Tokens >= services.Config.DailyTokenCeiling)
                throw new InvalidOperationException("Daily token ceiling reached.");
            var mode = RemotePolicy.ModeFor(services.Config, project, planOnly);
            var policy = services.CreatePolicy(); policy.WorkspaceRoot = project; policy.Unattended = !remote; policy.AllowRoutineUnattendedWork = !remote && !planOnly;
            policy.WorkspacePermission = remote ? mode.Permission : WorkspacePermission.Manual;
            policy.PermissionResolver = path => remote ? RemotePolicy.ModeFor(services.Config, path, planOnly).Permission : WorkspacePermission.Manual;
            var gate = new PermissionGate(policy, async (request, token) => remote && await approvals.AskAsync(request, (text, c) => Telegram!.SendAsync(services.Config.TelegramChatId, text, c), token) ? ApprovalAnswer.Approve : ApprovalAnswer.Deny);
            var loop = services.CreateLoop(gate); loop.Changes = Changes;
            var budget = new RunBudget(services.Config, services.UsageLog);
            var rotation = new OrchDesk.Core.Accounts.AccountRotator(services.Rotator.Accounts.Select(a => budget.Wrap(a.Provider)));
            var conversation = new Conversation(loop, rotation, project) { PreferredProviderId = string.IsNullOrWhiteSpace(preferredProvider) ? null : LegacyDefaults.ProviderId(preferredProvider) };
            var prompt = PromptContext.Build(services.Config, project, goal, services.Memory).Wrap(goal);
            var answer = new StringBuilder();
            await foreach (var ev in conversation.SendAsync(prompt, run.Token, readOnly: mode.PlanOnly))
            {
                log.Write(ev.GetType().Name, ev switch { TextDelta t => t.Text, StatusUpdate s => s.Text, ErrorEvent e => e.Message, _ => "" }, tier: conversation.LastProvider?.ModelId ?? "", role: "Agent");
                if (ev is TextDelta delta) { answer.Append(delta.Text); services.LiveStatus = services.LiveStatus! with { Output = answer.ToString().Split('\n').TakeLast(5).ToArray() }; }
                else if (ev is StatusUpdate status) services.LiveStatus = services.LiveStatus! with { Step = status.Text, Agents = [$"Agent {conversation.LastProvider?.ModelId ?? preferredProvider ?? "Auto"}: {status.Text}"] };
                else if (ev is ErrorEvent error) throw new InvalidOperationException(error.Message);
                else if (ev is UsageLimitHit) throw new UsageLimitException(DateTimeOffset.UtcNow.AddMinutes(15));
            }
            LastResult = services.SecretFile.Redact(answer.ToString());
            services.Reports.Add("automation", "update", goal[..Math.Min(goal.Length, 150)], LastResult, null, null, DateTime.UtcNow);
            summary = LastResult;
            return LastResult;
        }
        catch (Exception e) { result = e is UsageLimitException || e.Message.Contains("token ceiling", StringComparison.OrdinalIgnoreCase) ? "Usage limit" : e is OperationCanceledException ? "Stopped" : "Failed"; summary = e.Message; throw; }
        finally {
            log?.Write("complete", result + "\n" + summary);
            services.JobFinished(new(jobId, goal, project, result, DateTimeOffset.UtcNow - started, summary));
            services.LiveStatus = services.LiveStatus! with { Busy = false, Errors = result == "Done" ? [] : [summary] };
            run.Dispose(); run = null; Interlocked.Exchange(ref busy, 0);
        }
    }
    public void Stop() { run?.Cancel(); approvals.DenyAll(); }
    public string Resolve(bool approve, string? id) => approvals.Resolve(approve, id);
    public void Dispose() { services.Rebuilt -= RestartTelegram; timer.Stop(); poll?.Cancel(); Stop(); Supervisor.Stop(); Api.Dispose(); http.Dispose(); }
}

