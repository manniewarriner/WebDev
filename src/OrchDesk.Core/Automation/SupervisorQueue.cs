using System.Text.Json;
using OrchDesk.Core.Config;
using OrchDesk.Core.Safety;

namespace OrchDesk.Core.Automation;

// Ported from the original build's Supervisor (the "Task queue" page). State lives in supervisor-state.json with the same shape
// and enum numbers, so tasks queued by either build are picked up by the other.
public enum SupervisorState { Idle, Running, Paused, WaitingReset, Stopped }
public enum QueueTaskState { Queued, Running, WaitingReset, Completed, Failed, Stopped }

/// <summary>A provider reported a usage limit that resets at <see cref="ResetAt"/>; the queue waits instead of failing the task.</summary>
public sealed class UsageLimitException(DateTimeOffset resetAt) : Exception("Provider usage limit reached.") { public DateTimeOffset ResetAt { get; } = resetAt; }

public sealed class SupervisorTask
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Goal { get; set; } = "";
    public string Project { get; set; } = "";
    public string Model { get; set; } = "";
    /// <summary>Continuous tasks run again (up to the run/step/time caps) instead of stopping after one report.</summary>
    public bool Continuous { get; set; }
    public QueueTaskState Status { get; set; } = QueueTaskState.Queued;
    public int Step { get; set; }
    public int Runs { get; set; }
    public DateTimeOffset? ResetAt { get; set; }
    public string Reason { get; set; } = "";
    public string Summary => $"{Goal} · {Status} · step {Step}, runs {Runs}" + (ResetAt is { } reset ? $" · reset {reset.LocalDateTime:g}" : "") + (Reason.Length > 0 ? " · " + Reason : "");
}

/// <summary>
/// Runs queued goals one after another, unattended. Each task is capped by SupervisorMaxRuns runs, MaxSteps steps and
/// SupervisorMaxMinutes minutes; a usage limit pauses the task until the reported reset time.
/// </summary>
public sealed class SupervisorQueue
{
    private readonly AppConfig config;
    private readonly Func<SupervisorTask, CancellationToken, Task> run;
    private readonly Action? changed;
    private readonly string? statePath;
    private readonly Func<string, string> redact;
    private readonly Queue<SupervisorTask> queue = new();
    private readonly List<SupervisorTask> tasks = [];
    private CancellationTokenSource? cancellation;
    private Task? worker;
    private readonly object gate = new();
    public TimeSpan ContinuousPause { get; init; } = TimeSpan.FromSeconds(5);

    public SupervisorQueue(AppConfig config, Func<SupervisorTask, CancellationToken, Task> run, Action? changed = null, string? statePath = null, Func<string, string>? redact = null)
    {
        this.config = config; this.run = run; this.changed = changed; this.statePath = statePath; this.redact = redact ?? Redaction.Clean;
        if (statePath != null && File.Exists(statePath))
        {
            try
            {
                foreach (var task in JsonSerializer.Deserialize<List<SupervisorTask>>(File.ReadAllText(statePath)) ?? [])
                {
                    tasks.Add(task);
                    if (task.Status is QueueTaskState.Queued or QueueTaskState.Running or QueueTaskState.WaitingReset or QueueTaskState.Stopped) { task.Status = QueueTaskState.Queued; task.ResetAt = null; queue.Enqueue(task); }
                }
            }
            catch (Exception e) when (e is JsonException or IOException) { }
            State = queue.Count > 0 ? SupervisorState.Paused : SupervisorState.Idle;
        }
    }

    private void Notify()
    {
        if (statePath != null)
        {
            lock (gate)
            {
                try
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(statePath)!);
                    var safe = tasks.TakeLast(200).Select(t => new SupervisorTask { Id = t.Id, Goal = redact(t.Goal), Project = t.Project, Model = t.Model, Continuous = t.Continuous, Status = t.Status, Step = t.Step, Runs = t.Runs, ResetAt = t.ResetAt, Reason = redact(t.Reason) });
                    File.WriteAllText(statePath + ".tmp", JsonSerializer.Serialize(safe)); File.Move(statePath + ".tmp", statePath, true);
                }
                catch (IOException) { /* the queue keeps running; the next change saves again */ }
            }
        }
        changed?.Invoke();
    }

    public SupervisorState State { get; private set; }
    public IReadOnlyList<SupervisorTask> Tasks { get { lock (gate) return tasks.ToArray(); } }
    public int Pending { get { lock (gate) return queue.Count; } }

    public SupervisorTask Submit(string goal, string project, string model, bool continuous = false)
    {
        if (string.IsNullOrWhiteSpace(goal) || goal.Length > 100000 || !Directory.Exists(project)) throw new ArgumentException("Enter a goal and an existing project.");
        var task = new SupervisorTask { Goal = goal, Project = project, Model = model, Continuous = continuous };
        lock (gate) { queue.Enqueue(task); tasks.Add(task); }
        Notify(); return task;
    }

    public void Start()
    {
        lock (gate)
        {
            if (worker is { IsCompleted: false })
            {
                State = SupervisorState.Running;
                if (cancellation?.IsCancellationRequested == true) worker = RestartAfterAsync(worker);
            }
            else { State = SupervisorState.Running; cancellation?.Dispose(); cancellation = new(); worker = WorkSafelyAsync(cancellation.Token); }
        }
        Notify();
    }
    private async Task RestartAfterAsync(Task previous) { await previous.ConfigureAwait(false); cancellation?.Dispose(); cancellation = new(); await WorkSafelyAsync(cancellation.Token).ConfigureAwait(false); }
    private async Task WorkSafelyAsync(CancellationToken ct) { try { await WorkAsync(ct).ConfigureAwait(false); } catch (OperationCanceledException) when (ct.IsCancellationRequested) { } }
    public void Pause() { if (State is SupervisorState.Running or SupervisorState.WaitingReset) State = SupervisorState.Paused; Notify(); }
    public void Resume() => Start();
    public void Stop() { State = SupervisorState.Stopped; cancellation?.Cancel(); Notify(); }
    public async Task DrainAsync() { var current = worker; if (current != null) await current; }

    private async Task WorkAsync(CancellationToken ct)
    {
        await Task.Yield(); // let Start publish the worker before an empty queue finishes
        while (!ct.IsCancellationRequested)
        {
            while (State == SupervisorState.Paused) await Task.Delay(100, ct).ConfigureAwait(false);
            SupervisorTask? task; lock (gate) task = queue.TryDequeue(out var t) ? t : null;
            if (task == null) { State = SupervisorState.Idle; Notify(); return; }
            var minutes = Math.Max(1, config.SupervisorMaxMinutes);
            var deadline = DateTimeOffset.UtcNow.AddMinutes(minutes);
            using var taskCts = CancellationTokenSource.CreateLinkedTokenSource(ct); taskCts.CancelAfter(TimeSpan.FromMinutes(minutes));
            try
            {
                task.Status = QueueTaskState.Running; Notify();
                while (task.Runs < Math.Max(1, config.SupervisorMaxRuns) && task.Step < Math.Max(1, config.MaxSteps) && DateTimeOffset.UtcNow < deadline)
                {
                    while (State == SupervisorState.Paused) await Task.Delay(100, taskCts.Token).ConfigureAwait(false);
                    try
                    {
                        task.Step++; Notify(); await run(task, taskCts.Token).ConfigureAwait(false); task.Runs++;
                        if (!task.Continuous) { task.Status = QueueTaskState.Completed; task.Reason = "report"; break; }
                        await Task.Delay(ContinuousPause, taskCts.Token).ConfigureAwait(false);
                    }
                    catch (UsageLimitException limit)
                    {
                        task.ResetAt = limit.ResetAt; task.Status = QueueTaskState.WaitingReset; if (State != SupervisorState.Paused) State = SupervisorState.WaitingReset; Notify();
                        while (DateTimeOffset.UtcNow < limit.ResetAt) await Task.Delay(TimeSpan.FromSeconds(Math.Clamp((limit.ResetAt - DateTimeOffset.UtcNow).TotalSeconds, .01, 1)), taskCts.Token).ConfigureAwait(false);
                        task.ResetAt = null; task.Status = QueueTaskState.Running; if (State != SupervisorState.Paused) State = SupervisorState.Running; Notify();
                    }
                }
                if (task.Status != QueueTaskState.Completed) { task.Status = QueueTaskState.Completed; task.Reason = "run/step/time cap reached"; }
            }
            catch (OperationCanceledException) { task.Status = ct.IsCancellationRequested ? QueueTaskState.Stopped : QueueTaskState.Failed; task.Reason = ct.IsCancellationRequested ? "stopped" : "time cap"; }
            catch (Exception e) { task.Status = QueueTaskState.Failed; var why = redact(e.Message).ReplaceLineEndings(" "); task.Reason = why.Length == 0 ? "run failed; review chat activity" : why[..Math.Min(why.Length, 160)]; }
            Notify();
        }
    }
}
