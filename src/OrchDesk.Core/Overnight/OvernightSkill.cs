using System.Text.Json;
using System.Text.Json.Nodes;
using OrchDesk.Core.Agent;
using OrchDesk.Core.Models;
using OrchDesk.Core.Permissions;
using OrchDesk.Core.Providers;
using OrchDesk.Core.Tools;

namespace OrchDesk.Core.Overnight;

public sealed class OvernightRun
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Goal { get; set; } = "";
    public string Workspace { get; set; } = "";
    public string ManagerId { get; set; } = "";
    public List<string> WorkerIds { get; set; } = [];
    public int MaxRounds { get; set; } = 4;
    public int Round { get; set; }
    public bool ShutdownWhenDone { get; set; }
    public string TestCommand { get; set; } = "dotnet test";
    public DateTimeOffset Deadline { get; set; } = DateTimeOffset.UtcNow.AddHours(8);
    public int MaxSteps { get; set; } = 40;
    public int Steps { get; set; }
    public string Status { get; set; } = "Running";
    public bool TestsPassed { get; set; }
    public string Summary { get; set; } = "";
    public List<ChatMessage> Messages { get; set; } = [];
    public List<ToolCall> PendingTools { get; set; } = [];
    public ToolCall? InFlight { get; set; }
    public List<ApprovalRequest> Approvals { get; set; } = [];
}

/// <summary>A bounded manager skill. Only host-controlled tools may run unattended.</summary>
public sealed class OvernightSkill(string directory, ToolRegistry tools, PermissionPolicy policy)
{
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };
    public event Action<ChatEvent>? Progress;
    public string PathFor(string id) => Path.Combine(directory, id + ".json");
    public OvernightRun Load(string path) => JsonSerializer.Deserialize<OvernightRun>(File.ReadAllText(path))!;
    public void Save(OvernightRun run)
    {
        Directory.CreateDirectory(directory);
        var path = PathFor(run.Id);
        File.WriteAllText(path + ".tmp", JsonSerializer.Serialize(run, Json));
        File.Move(path + ".tmp", path, true);
    }

    public async Task<string> RunAsync(OvernightRun run, IModelProvider manager, CancellationToken ct)
    {
        if (manager.Id != run.ManagerId) throw new InvalidOperationException("Resume requires the original manager model.");
        if (manager.IsAgentic) throw new InvalidOperationException("Overnight requires a model with host-controlled tools; choose an API model.");
        run.Status = "Running";
        if (run.Messages.Count == 0)
        {
            run.Messages.Add(ChatMessage.System(AgentLoop.DefaultSystemPrompt +
                " Overnight run: first explain your plan, then work step by step. Approval requests are queued for morning; continue independent work. Finish only when the goal and tests are complete."));
            run.Messages.Add(ChatMessage.User(run.Goal));
        }
        // An interrupted side effect has unknown outcome. Never silently replay it.
        if (run.InFlight is { } interrupted)
        {
            run.Approvals.Add(new(interrupted.Name, interrupted.ArgumentsJson, "Interrupted action: inspect outcome before retrying"));
            if (interrupted.Id.StartsWith("tests-")) run.Messages.Add(ChatMessage.User("Host verification was interrupted; queued for morning review."));
            else run.Messages.Add(ChatMessage.ToolResult(interrupted.Id, interrupted.Name, "Interrupted; queued for morning review."));
            run.PendingTools.RemoveAll(t => t.Id == interrupted.Id);
            run.InFlight = null;
        }
        var gate = new PermissionGate(policy, (request, _) => {
            if (!run.Approvals.Contains(request)) run.Approvals.Add(request);
            Save(run);
            return Task.FromResult(ApprovalAnswer.Deny);
        });
        var loop = new AgentLoop(tools, gate);
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var remaining = run.Deadline - DateTimeOffset.UtcNow;
        if (remaining <= TimeSpan.Zero) deadline.Cancel();
        else deadline.CancelAfter(remaining);
        Save(run);
        try
        {
            while (run.PendingTools.Count > 0 || run.Steps < run.MaxSteps)
            {
                deadline.Token.ThrowIfCancellationRequested();
                foreach (var call in run.PendingTools.ToList())
                {
                    run.InFlight = call;
                    Save(run);
                    var result = await loop.ExecuteToolAsync(call, new ToolContext(run.Workspace), deadline.Token);
                    run.Messages.Add(ChatMessage.ToolResult(call.Id, call.Name, result.Output));
                    run.PendingTools.Remove(call);
                    run.InFlight = null;
                    Save(run);
                    Progress?.Invoke(new ToolCallResult(call.Id, call.Name, result.Output, result.IsError));
                }
                if (run.Steps >= run.MaxSteps) break;
                run.Steps++;
                Save(run); // Budget is consumed even if the provider is interrupted.
                var text = new System.Text.StringBuilder();
                var calls = new List<ToolCall>();
                await foreach (var ev in manager.StreamChatAsync(new ChatRequest(run.Messages, tools.Definitions, run.Workspace), deadline.Token))
                {
                    Progress?.Invoke(ev);
                    if (ev is TextDelta d) text.Append(d.Text);
                    if (ev is ToolCallRequested t) calls.Add(t.Call);
                    if (ev is ErrorEvent or ProviderUnavailable or UsageLimitHit) throw new InvalidOperationException("Manager unavailable; checkpoint retained.");
                }
                run.Messages.Add(ChatMessage.Assistant(text.ToString(), calls));
                run.PendingTools = calls;
                run.Summary = text.ToString();
                Save(run);
                if (calls.Count > 0) continue;
                // Verification uses the same approval gate as model tools.
                var testCall = new ToolCall("tests-" + run.Steps, "run_command", new JsonObject { ["command"] = run.TestCommand }.ToJsonString());
                run.InFlight = testCall;
                Save(run);
                var test = await loop.ExecuteToolAsync(testCall, new ToolContext(run.Workspace), deadline.Token);
                run.InFlight = null;
                run.TestsPassed = !test.IsError && test.Output.StartsWith("Exit code: 0\n", StringComparison.OrdinalIgnoreCase);
                run.Messages.Add(ChatMessage.User("Host verification result: " + test.Output));
                Save(run);
                if (run.TestsPassed) { run.Status = run.Approvals.Count == 0 ? "Completed" : "Needs morning approval"; break; }
                run.Messages.Add(ChatMessage.User("Verification did not pass. Fix failures and continue: " + test.Output));
            }
            if (run.Status == "Running") run.Status = "Step budget reached";
        }
        catch (OperationCanceledException) { run.Status = ct.IsCancellationRequested ? "Paused" : "Deadline reached"; }
        catch (InvalidOperationException) { run.Status = "Manager unavailable"; run.Summary = "Manager stopped; progress is checkpointed for review."; }
        finally { Save(run); }
        var report = $"# Morning summary\n\n{run.Goal}\n\nStatus: {run.Status}\nSteps: {run.Steps}/{run.MaxSteps}\nTests passed: {run.TestsPassed}\n\n{run.Summary}\n\n## Queued approvals\n" +
            string.Join("\n", run.Approvals.Select(a => $"- {a.Summary}: {a.Reason}"));
        await File.WriteAllTextAsync(Path.Combine(directory, run.Id + ".md"), report, CancellationToken.None);
        return report;
    }
}
