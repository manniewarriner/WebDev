using System.Text;
using System.Text.Json;
using OrchDesk.Core.Agent;
using OrchDesk.Core.Config;
using OrchDesk.Core.Models;
using OrchDesk.Core.Permissions;
using OrchDesk.Core.Providers;
using OrchDesk.Core.Tools;
namespace OrchDesk.Core.Overnight;

/// <summary>Plan, edit, verify and commit on the selected branch. Never pushes.</summary>
public sealed class OvernightRoundRunner(string directory, ToolRegistry tools, PermissionPolicy policy)
{
    public event Action<ChatEvent>? Progress;
    private static Task<CommandResult> Git(string workspace, CancellationToken ct, params string[] args) => ProcessRunner.RunAsync(ProcessRunner.Exe("git", args, workspace), TimeSpan.FromMinutes(2), ct);
    public async Task<string> RunAsync(OvernightRun run, IReadOnlyList<IModelProvider> providers, CancellationToken ct)
    {
        var manager = providers.FirstOrDefault(p => p.Id == run.ManagerId) ?? throw new InvalidOperationException("Manager is not configured");
        var workers = (run.WorkerIds.Count == 0 ? new List<string> { run.ManagerId } : run.WorkerIds).Select(id => providers.FirstOrDefault(p => p.Id == id) ?? throw new InvalidOperationException("Worker is not configured: " + id)).ToList();
        if (workers.Any(p => p.IsAgentic)) throw new InvalidOperationException("Unattended workers must use host-controlled API tools.");
        var folder = Path.Combine(directory, run.Id); Directory.CreateDirectory(folder);
        var path = Path.Combine(folder, "checkpoint.json");
        void Save() { File.WriteAllText(path + ".tmp", JsonSerializer.Serialize(run)); File.Move(path + ".tmp", path, true); }
        void Emit(ChatEvent ev) { Progress?.Invoke(ev); var entry = ev switch { StatusUpdate status => status.Text, ToolCallRequested call => "Tool requested: " + call.Call.Name, ToolCallResult result => "Tool finished: " + result.Name + (result.IsError ? " (error)" : ""), _ => null }; if (entry is not null) File.AppendAllText(Path.Combine(folder, "progress.log"), $"{DateTimeOffset.Now:O} {entry}\n"); }
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var remaining = run.Deadline - DateTimeOffset.UtcNow;
        if (remaining <= TimeSpan.Zero) deadline.Cancel(); else deadline.CancelAfter(remaining);
        run.Status = "Running"; Save();
        try {
            var branch = await Git(run.Workspace, deadline.Token, "symbolic-ref", "--short", "HEAD");
            var clean = await Git(run.Workspace, deadline.Token, "status", "--porcelain");
            if (branch.ExitCode != 0 || clean.ExitCode != 0 || (run.Round == 0 && !string.IsNullOrWhiteSpace(clean.Output))) throw new InvalidOperationException("Overnight requires a clean working tree on a branch; checkpoint retained.");
            var originalBranch = branch.Output.Trim();
            var gate = new PermissionGate(policy, (request, _) => { run.Approvals.Add(request); Save(); return Task.FromResult(ApprovalAnswer.Deny); });
            policy.HostManagedCommits = true;
            var loop = new AgentLoop(tools, gate, new AgentLoopOptions { MaxSteps = run.MaxSteps });
            while (run.Round < run.MaxRounds) {
                deadline.Token.ThrowIfCancellationRequested(); run.Round++; Save();
                Emit(new StatusUpdate($"Round {run.Round}/{run.MaxRounds}: planning"));
                var plan = new StringBuilder();
                await foreach (var ev in manager.StreamChatAsync(new ChatRequest([ChatMessage.System("Plan the next small implementation round. Return instructions only. Do not edit files or run commands."), ChatMessage.User(run.Goal + "\nPrevious result: " + run.Summary)], [], run.Workspace, ReadOnly: true), deadline.Token)) {
                    if (ev is TextDelta d) plan.Append(d.Text);
                    if (ev is ErrorEvent or UsageLimitHit or ProviderUnavailable) throw new InvalidOperationException("Manager unavailable");
                    Emit(ev);
                }
                if (plan.Length == 0) throw new InvalidOperationException("Manager returned no plan");
                foreach (var worker in workers) {
                    Emit(new StatusUpdate("Worker " + worker.Id));
                    var text = new StringBuilder();
                    await foreach (var ev in loop.RunAsync(worker, [ChatMessage.System(AgentLoop.DefaultSystemPrompt + " Never commit, switch branches or push. Work only on this round's instructions. Do not touch CRM code or Claude profile files."), ChatMessage.User(run.Goal + "\nRound plan:\n" + plan)], new ToolContext(run.Workspace), deadline.Token)) {
                        Emit(ev); if (ev is TextDelta d) text.Append(d.Text);
                        if (ev is ErrorEvent or ProviderUnavailable or UsageLimitHit) throw new InvalidOperationException("Worker unavailable");
                    }
                    run.Summary = text.ToString(); Save();
                }
                Emit(new StatusUpdate("Build and test"));
                foreach (var command in new[] { "dotnet build", run.TestCommand }) {
                    var result = await loop.ExecuteToolAsync(new ToolCall("verify-" + run.Round, "run_command", new System.Text.Json.Nodes.JsonObject { ["command"] = command }.ToJsonString()), new ToolContext(run.Workspace), deadline.Token);
                    run.TestsPassed = !result.IsError && result.Output.StartsWith("Exit code: 0\n", StringComparison.OrdinalIgnoreCase);
                    if (!run.TestsPassed) { run.Summary += "\nVerification failed:\n" + result.Output; break; }
                }
                Save();
                if (!run.TestsPassed) { Emit(new StatusUpdate("Verification failed; changes retained without commit")); continue; }
                var current = await Git(run.Workspace, deadline.Token, "symbolic-ref", "--short", "HEAD");
                if (current.Output.Trim() != originalBranch) throw new InvalidOperationException("Branch changed; commit refused");
                var add = await Git(run.Workspace, deadline.Token, "add", "-A");
                if (add.ExitCode != 0) throw new InvalidOperationException("Staging failed");
                var staged = await Git(run.Workspace, deadline.Token, "--no-pager", "diff", "--cached");
                if (staged.ExitCode != 0 || SecretScanner.FindSecrets(staged.Output).Any()) {
                    await Git(run.Workspace, CancellationToken.None, "reset");
                    throw new InvalidOperationException("Secret scan failed; commit refused");
                }
                var diff = await Git(run.Workspace, deadline.Token, "diff", "--cached", "--quiet");
                if (diff.ExitCode == 1) {
                    var commit = await Git(run.Workspace, deadline.Token, "commit", "-m", $"OrchDesk overnight round {run.Round}");
                    if (commit.ExitCode != 0) throw new InvalidOperationException("Commit failed");
                    Emit(new StatusUpdate("Green round committed to " + originalBranch));
                } else if (diff.ExitCode != 0) throw new InvalidOperationException("Diff failed");
                run.Status = run.Approvals.Count == 0 ? "Completed" : "Needs morning approval"; Save(); break;
            }
            if (run.Status == "Running") run.Status = "Round budget reached";
        } catch (OperationCanceledException) { run.Status = ct.IsCancellationRequested ? "Paused" : "Deadline reached"; }
        catch (InvalidOperationException ex) { run.Status = "Needs attention"; run.Summary = ex.Message; }
        finally { Save(); }
        var report = $"# Morning summary\n\n{run.Goal}\n\nStatus: {run.Status}\nRounds: {run.Round}/{run.MaxRounds}\nTests passed: {run.TestsPassed}\n\n{run.Summary}\n\nQueued approvals: {run.Approvals.Count}\n";
        await File.WriteAllTextAsync(Path.Combine(folder, "morning-summary.md"), report, CancellationToken.None);
        Emit(new StatusUpdate("Morning summary written"));
        if (run.ShutdownWhenDone && run.Status is not ("Paused" or "Needs attention")) {
            Emit(new StatusUpdate("Shutdown scheduled in 120 seconds"));
            var shutdown = await ProcessRunner.RunAsync(ProcessRunner.Exe("shutdown", ["/s", "/t", "120"], run.Workspace), TimeSpan.FromSeconds(10), CancellationToken.None);
            if (shutdown.ExitCode != 0) Emit(new StatusUpdate("Shutdown command failed"));
        }
        return report;
    }
}
