using OrchDesk.Core.Automation;
namespace OrchDesk.Tests.Baseline;
public sealed class SupervisorQueueTests : IDisposable {
    private readonly string root = Path.Combine(Path.GetTempPath(), "orch-queue-" + Guid.NewGuid().ToString("N"));
    public SupervisorQueueTests() => Directory.CreateDirectory(root);
    public void Dispose() => Directory.Delete(root, true);
    [Fact] public async Task SupervisorQueuePauseResumeAndCaps()
    {
        int runs = 0; var supervisor = new SupervisorQueue(new() { SupervisorMaxRuns = 1 }, (_, _) => { runs++; return Task.CompletedTask; });
        var a = supervisor.Submit("one", root, "test"); var b = supervisor.Submit("two", root, "test"); supervisor.Start(); await supervisor.DrainAsync();
        Assert.Equal(2, runs); Assert.Equal(QueueTaskState.Completed, a.Status); Assert.Equal(QueueTaskState.Completed, b.Status); Assert.Equal(SupervisorState.Idle, supervisor.State);
        supervisor.Stop(); Assert.Equal(SupervisorState.Stopped, supervisor.State); supervisor.Resume(); await supervisor.DrainAsync(); Assert.Equal(SupervisorState.Idle, supervisor.State);
    }
    [Fact] public async Task SupervisorWaitsForLimitReset()
    {
        int attempts = 0; var waiting = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously); SupervisorQueue? supervisor = null;
        supervisor = new SupervisorQueue(new(), (_, _) => { if (++attempts == 1) throw new UsageLimitException(DateTimeOffset.UtcNow.AddMilliseconds(120)); return Task.CompletedTask; }, () => { if (supervisor?.State == SupervisorState.WaitingReset) waiting.TrySetResult(); });
        var task = supervisor.Submit("wait", root, "test"); supervisor.Start(); await waiting.Task.WaitAsync(TimeSpan.FromSeconds(5)); Assert.Equal(QueueTaskState.WaitingReset, task.Status); await supervisor.DrainAsync(); Assert.Equal(QueueTaskState.Completed, task.Status); Assert.Equal(1, task.Runs);
    }
    [Fact] public async Task SupervisorStopCancelsCurrentRun()
    {
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var supervisor = new SupervisorQueue(new(), async (_, ct) => { started.TrySetResult(); await Task.Delay(Timeout.Infinite, ct); });
        var task = supervisor.Submit("stop", root, "test"); supervisor.Start(); await started.Task.WaitAsync(TimeSpan.FromSeconds(5)); supervisor.Stop(); await supervisor.DrainAsync(); Assert.Equal(QueueTaskState.Stopped, task.Status);
    }
    [Fact] public async Task SupervisorPausePreventsNextTaskUntilResume()
    {
        var firstStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously); var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously); int runs = 0;
        var supervisor = new SupervisorQueue(new(), async (_, _) => { if (Interlocked.Increment(ref runs) == 1) { firstStarted.TrySetResult(); await release.Task; } });
        supervisor.Submit("first", root, "test"); supervisor.Submit("second", root, "test"); supervisor.Start(); await firstStarted.Task.WaitAsync(TimeSpan.FromSeconds(5)); supervisor.Pause(); release.TrySetResult();
        await Task.Delay(150); Assert.Equal(1, runs); Assert.Equal(SupervisorState.Paused, supervisor.State); supervisor.Resume(); await supervisor.DrainAsync(); Assert.Equal(2, runs);
    }
    [Fact] public void SupervisorRestoresInterruptedQueuePausedAndRedactsGoals()
    {
        var file = Path.Combine(root, "supervisor.json"); var supervisor = new SupervisorQueue(new(), (_, _) => Task.CompletedTask, statePath: file, redact: s => s.Replace("dummy-secret", "[REDACTED]"));
        supervisor.Submit("task dummy-secret", root, "test"); Assert.DoesNotContain("dummy-secret", File.ReadAllText(file));
        var restored = new SupervisorQueue(new(), (_, _) => Task.CompletedTask, statePath: file); Assert.Equal(SupervisorState.Paused, restored.State); Assert.Equal(QueueTaskState.Queued, Assert.Single(restored.Tasks).Status);
    }
}