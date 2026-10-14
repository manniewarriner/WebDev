using System.Text;
using OrchDesk.Core.Accounts;
using OrchDesk.Core.Agent;
using OrchDesk.Core.Config;
using OrchDesk.Core.Models;
using OrchDesk.Core.Orchestration;
using OrchDesk.Core.Permissions;
using OrchDesk.Core.Tools;

namespace OrchDesk.Core.Overnight;

/// <summary>
/// Works through the queue with nobody watching:
///   • each task runs in its own git worktree on a new branch orchdesk/&lt;date&gt;/&lt;id&gt; — your checkout and main are never touched;
///   • the permission policy is in unattended mode, so anything that would normally ask is refused;
///   • models rotate automatically on usage limits / PC offline (same chain as the chat);
///   • after each attempt OrchDesk runs the tests itself and feeds failures back, up to MaxAttempts;
///   • results are committed on the branch; pushing is optional, only orchdesk/* branches, and only after a secret scan;
///   • a Markdown morning report summarises everything.
/// </summary>
public sealed class OvernightRunner
{
    private readonly OvernightQueueStore _store;
    private readonly AccountRotator _rotator;
    private readonly OvernightConfig _cfg;
    private readonly SupervisorConfig _sup;
    private readonly AgentLoop _loop;
    private readonly string _reportsDir;
    private List<OvernightTask> _tasks = [];

    public OvernightRunner(OvernightQueueStore store, ToolRegistry tools, AccountRotator rotator,
        OvernightConfig cfg, SupervisorConfig sup, int maxSteps, string reportsDir)
    {
        _store = store;
        _rotator = rotator;
        _cfg = cfg;
        _sup = sup;
        _reportsDir = reportsDir;
        var policy = new PermissionPolicy { Unattended = true };
        _loop = new AgentLoop(tools, new PermissionGate(policy, approver: null), new AgentLoopOptions { MaxSteps = maxSteps });
    }

    public event Action<string>? Progress;
    public bool IsRunning { get; private set; }

    public async Task<string> RunAllAsync(CancellationToken ct)
    {
        IsRunning = true;
        using var awake = _cfg.KeepPcAwake ? new KeepAwake() : null;
        var started = DateTimeOffset.Now;
        var ran = new List<OvernightTask>();
        try
        {
            _tasks = _store.Load();
            await _rotator.RefreshHealthAsync(ct);
            foreach (var task in _tasks.Where(t => t.Status == OvernightStatus.Queued).ToList())
            {
                if (ct.IsCancellationRequested) break;
                ran.Add(task);
                await RunTaskAsync(task, ct);
                _store.Save(_tasks);
            }
        }
        finally
        {
            IsRunning = false;
            _store.Save(_tasks);
        }
        return MorningReport.Write(_reportsDir, started, ran, _rotator);
    }

    private void Say(OvernightTask task, string text)
    {
        Progress?.Invoke($"[{task.Title}] {text}");
        if (task.LogPath is not null) File.AppendAllText(task.LogPath, $"{DateTimeOffset.Now:HH:mm:ss} {text}\n");
    }

    private static Task<CommandResult> Git(string cwd, CancellationToken ct, params string[] args) =>
        ProcessRunner.RunAsync(ProcessRunner.Exe("git", args, cwd), TimeSpan.FromMinutes(3), ct);

    public async Task RunTaskAsync(OvernightTask task, CancellationToken ct)
    {
        task.Status = OvernightStatus.Running;
        task.StartedAt ??= DateTimeOffset.Now;
        Directory.CreateDirectory(Path.Combine(_store.Directory, "logs"));
        task.LogPath = Path.Combine(_store.Directory, "logs", task.Id + ".log");
        _store.Save(_tasks);

        try
        {
            var top = await Git(task.Workspace, ct, "rev-parse", "--show-toplevel");
            if (top.ExitCode != 0) { Fail(task, OvernightStatus.NeedsAttention, "Workspace is not a git repository (overnight mode needs git for safe branches)."); return; }

            var baseSha = (await Git(task.Workspace, ct, "rev-parse", "HEAD")).Output.Trim();
            task.Branch ??= $"orchdesk/{DateTime.Now:yyyyMMdd}/{task.Id[^6..]}";
            task.WorktreePath ??= Path.Combine(_store.Directory, "worktrees", task.Id);

            if (!Directory.Exists(task.WorktreePath))
            {
                var add = await Git(task.Workspace, ct, "worktree", "add", "-b", task.Branch, task.WorktreePath, "HEAD");
                if (add.ExitCode != 0) { Fail(task, OvernightStatus.NeedsAttention, "git worktree add failed: " + add.Output); return; }
            }
            else Say(task, "Resuming in existing worktree.");

            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(TimeSpan.FromMinutes(_cfg.MaxMinutesPerTask));

            string? failure = null;
            while (task.Attempts < _cfg.MaxAttemptsPerTask)
            {
                task.Attempts++;
                Say(task, $"Attempt {task.Attempts}/{_cfg.MaxAttemptsPerTask}");
                var prompt = failure is null
                    ? task.Prompt
                    : $"{task.Prompt}\n\nYour previous attempt fails the tests (`{task.TestCommand}`):\n{Orchestration.Supervisor.Tail(failure, 5000)}\nFix it.";

                var (ok, summary) = await RunAgentAsync(task, prompt, timeout.Token);
                task.Summary = summary;
                if (!ok) break;

                if (string.IsNullOrWhiteSpace(task.TestCommand)) { task.TestsPassed = null; break; }
                Say(task, $"Running tests: {task.TestCommand}");
                var test = await ProcessRunner.RunAsync(ProcessRunner.Shell(task.TestCommand, task.WorktreePath), TimeSpan.FromMinutes(15), timeout.Token);
                task.TestsPassed = test.ExitCode == 0;
                Say(task, ProcessRunner.Format(test));
                if (task.TestsPassed == true) break;
                failure = test.Output;
            }

            // Commit whatever was produced so nothing is lost, even when tests fail.
            await Git(task.WorktreePath, ct, "add", "-A");
            var status = await Git(task.WorktreePath, ct, "status", "--porcelain");
            if (status.Output.Trim().Length > 0)
                await Git(task.WorktreePath, ct, "commit", "-m", $"OrchDesk overnight: {task.Title}\n\n{task.Prompt}");
            task.DiffStat = (await Git(task.WorktreePath, ct, "--no-pager", "diff", "--stat", $"{baseSha}..HEAD")).Output.Trim();

            if (_cfg.PushBranches && task.TestsPassed != false && task.DiffStat.Length > 0)
                await PushAsync(task, baseSha, ct);

            task.Status = task.TestsPassed == false ? OvernightStatus.Failed
                : task.DiffStat.Length == 0 ? OvernightStatus.NeedsAttention
                : task.Error is not null ? OvernightStatus.NeedsAttention
                : OvernightStatus.Succeeded;
            if (task.DiffStat.Length == 0) task.Notes.Add("No changes were made.");
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            Fail(task, OvernightStatus.NeedsAttention, $"Timed out after {_cfg.MaxMinutesPerTask} minutes.");
        }
        catch (OperationCanceledException)
        {
            task.Status = OvernightStatus.Cancelled;
            throw;
        }
        catch (Exception ex)
        {
            Fail(task, OvernightStatus.Failed, ex.Message);
        }
        finally
        {
            task.FinishedAt = DateTimeOffset.Now;
            Say(task, $"Finished: {task.Status}");
        }
    }

    private async Task<(bool Ok, string Summary)> RunAgentAsync(OvernightTask task, string prompt, CancellationToken ct)
    {
        var text = new StringBuilder();
        IAsyncEnumerable<ChatEvent> events;
        if (task.Mode == OvernightMode.Supervisor)
        {
            var planner = _rotator.Find(_sup.PlannerProviderId)?.Provider;
            var worker = _rotator.Find(_sup.WorkerProviderId) is { } w && _rotator.IsUsable(w) ? w.Provider : _rotator.Current();
            if (planner is null || worker is null) { task.Error = "Supervisor planner/worker model not available."; return (false, ""); }
            events = new Orchestration.Supervisor(_loop) { MaxRounds = _sup.MaxRounds }
                .RunAsync(prompt, planner, worker, task.TestCommand ?? _sup.TestCommand, task.WorktreePath!, ct);
        }
        else
        {
            var convo = new Conversation(_loop, _rotator, task.WorktreePath!) { PreferredProviderId = task.PreferredProviderId };
            events = convo.SendAsync(prompt, ct);
        }

        await foreach (var ev in events)
        {
            switch (ev)
            {
                case TextDelta d: text.Append(d.Text); break;
                case StatusUpdate s:
                    Say(task, s.Text);
                    if (s.Text.StartsWith("Using ")) task.ProviderUsed = s.Text[6..];
                    break;
                case ToolCallRequested r: Say(task, $"→ {r.Call.Name} {Trim(r.Call.ArgumentsJson, 300)}"); break;
                case ToolCallResult r: Say(task, $"← {r.Name}{(r.IsError ? " (error)" : "")}: {Trim(r.Output, 500)}"); break;
                case ErrorEvent e: task.Error = e.Message; Say(task, "ERROR " + e.Message); return (false, text.ToString());
            }
        }
        task.Error = null;
        return (true, Trim(text.ToString().Trim(), 2000));
    }

    private async Task PushAsync(OvernightTask task, string baseSha, CancellationToken ct)
    {
        if (task.Branch is null || !task.Branch.StartsWith("orchdesk/")) { task.Notes.Add("Push skipped: not an orchdesk/* branch."); return; }
        var scan = await SecretScanner.ScanBranchAsync(task.WorktreePath!, baseSha, ct);
        if (!scan.Clean) { task.Notes.Add("Push blocked by secret scan: " + scan.Details); return; }
        var push = await Git(task.WorktreePath!, ct, "push", "-u", "origin", task.Branch);
        task.Pushed = push.ExitCode == 0;
        task.Notes.Add(task.Pushed ? $"Pushed {task.Branch}." : "Push failed: " + Trim(push.Output, 500));
    }

    private void Fail(OvernightTask task, OvernightStatus status, string error)
    {
        task.Status = status;
        task.Error = error;
        Say(task, error);
    }

    private static string Trim(string s, int max) => s.Length <= max ? s : s[..max] + "…";
}
