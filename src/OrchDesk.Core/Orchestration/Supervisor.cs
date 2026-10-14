using System.Runtime.CompilerServices;
using System.Text;
using OrchDesk.Core.Agent;
using OrchDesk.Core.Models;
using OrchDesk.Core.Providers;
using OrchDesk.Core.Tools;

namespace OrchDesk.Core.Orchestration;

/// <summary>
/// "Y1" mode: an expensive model (Claude) plans and reviews; a cheap model (PC/OpenCode) does the bulk edits.
///   1. Planner reads the repo (read-only) and writes a step-by-step plan.
///   2. Worker carries the plan out with the tools.
///   3. OrchDesk runs the test command itself.
///   4. If tests fail, the planner reviews the failure + diff and writes fix instructions; back to 2.
/// Stops when tests pass or after MaxRounds. Error context accumulates in the worker's conversation.
/// </summary>
public sealed class Supervisor(AgentLoop loop)
{
    public int MaxRounds { get; init; } = 4;
    public TimeSpan TestTimeout { get; init; } = TimeSpan.FromMinutes(10);

    public sealed record Outcome(bool Passed, int Rounds, string LastTestOutput);
    public Outcome? Result { get; private set; }

    public async IAsyncEnumerable<ChatEvent> RunAsync(
        string task, IModelProvider planner, IModelProvider worker, string testCommand, string workspace,
        [EnumeratorCancellation] CancellationToken ct)
    {
        Result = null;
        var ctx = new ToolContext(workspace);
        var plannerMessages = new List<ChatMessage>
        {
            ChatMessage.User(
                "You are the planner/reviewer. Do NOT edit files. Inspect the project and write a precise, numbered plan " +
                "a less capable coding model can follow: which files to change, what to change, and how to verify.\n\n" +
                $"Task: {task}\nTest command: {testCommand}"),
        };

        yield return new StatusUpdate($"Planning with {planner.DisplayName}…");
        var plan = new StringBuilder();
        await foreach (var ev in loop.RunAsync(planner, plannerMessages, ctx, ct, readOnly: true))
        {
            if (ev is TextDelta d) plan.Append(d.Text);
            yield return ev;
            if (ev is ErrorEvent or UsageLimitHit or ProviderUnavailable) yield break;
        }

        var workerMessages = new List<ChatMessage>
        {
            ChatMessage.User($"Implement this plan in the project. Task: {task}\n\nPlan:\n{plan}\n\nWhen done, run `{testCommand}`."),
        };

        var lastOutput = "";
        for (var round = 1; round <= MaxRounds; round++)
        {
            yield return new StatusUpdate($"Round {round}/{MaxRounds}: {worker.DisplayName} is coding…");
            await foreach (var ev in loop.RunAsync(worker, workerMessages, ctx, ct))
            {
                yield return ev;
                if (ev is ErrorEvent or UsageLimitHit or ProviderUnavailable) yield break;
            }

            yield return new StatusUpdate($"Running tests: {testCommand}");
            var approval = await loop.ExecuteToolAsync(new ToolCall($"test-{round}", "run_command",
                System.Text.Json.JsonSerializer.Serialize(new { command = testCommand })),
                new ToolContext(workspace) { DefaultCommandTimeout = TestTimeout }, ct);
            if (approval.IsError) { yield return new ErrorEvent(approval.Output); yield break; }
            // run_command reports the exit code; preserve failed output for the review round.
            var test = new CommandResult(approval.Output.StartsWith("Exit code: 0\n", StringComparison.Ordinal) ? 0 : 1, approval.Output, false);
            lastOutput = test.Output;
            yield return new ToolCallResult($"test-{round}", "tests", ProcessRunner.Format(test), test.ExitCode != 0);
            if (test.ExitCode == 0)
            {
                Result = new Outcome(true, round, lastOutput);
                yield return new Done($"Tests pass after {round} round(s).");
                yield break;
            }
            if (round == MaxRounds) break;

            yield return new StatusUpdate($"Tests failed — {planner.DisplayName} is reviewing…");
            var diff = await ProcessRunner.RunAsync(ProcessRunner.Exe("git", ["--no-pager", "diff"], ctx.WorkspaceRoot), TimeSpan.FromMinutes(1), ct);
            plannerMessages.Add(ChatMessage.User(
                $"The worker's changes fail the tests. Review and give concise fix instructions (no edits yourself).\n\n" +
                $"Test output (tail):\n{Tail(test.Output, 6000)}\n\nDiff:\n{Tail(diff.Output, 8000)}"));
            var review = new StringBuilder();
            await foreach (var ev in loop.RunAsync(planner, plannerMessages, ctx, ct, readOnly: true))
            {
                if (ev is TextDelta d) review.Append(d.Text);
                yield return ev;
                if (ev is ErrorEvent or UsageLimitHit or ProviderUnavailable) yield break;
            }
            workerMessages.Add(ChatMessage.User(
                $"Tests failed:\n{Tail(test.Output, 4000)}\n\nReviewer's fix instructions:\n{review}\n\nApply the fixes and rerun `{testCommand}`."));
        }

        Result = new Outcome(false, MaxRounds, lastOutput);
        yield return new ErrorEvent($"Tests still failing after {MaxRounds} rounds. The last output is above.");
    }

    internal static string Tail(string s, int max) => s.Length <= max ? s : "…" + s[^max..];
}

/// <summary>
/// Cost router: try the cheapest model first, verify with the tests, and step up a tier only on failure.
/// The next tier works on the same folder and is told what the previous attempt broke.
/// </summary>
public sealed class CostRouter(AgentLoop loop)
{
    public TimeSpan TestTimeout { get; init; } = TimeSpan.FromMinutes(10);

    public async IAsyncEnumerable<ChatEvent> RunAsync(
        string task, IReadOnlyList<IModelProvider> tiers, string testCommand, string workspace,
        [EnumeratorCancellation] CancellationToken ct)
    {
        var ctx = new ToolContext(workspace);
        string? previousFailure = null;
        foreach (var tier in tiers)
        {
            yield return new StatusUpdate($"Trying {tier.DisplayName}…");
            var prompt = previousFailure is null
                ? $"{task}\n\nWhen done, run `{testCommand}`."
                : $"{task}\n\nA cheaper model already attempted this and the tests still fail:\n{Supervisor.Tail(previousFailure, 4000)}\n" +
                  $"Fix the code so `{testCommand}` passes.";
            var messages = new List<ChatMessage> { ChatMessage.User(prompt) };
            var skip = false;
            await foreach (var ev in loop.RunAsync(tier, messages, ctx, ct))
            {
                yield return ev;
                if (ev is UsageLimitHit or ProviderUnavailable) { skip = true; break; }
            }
            if (skip) continue;

            var test = await ProcessRunner.RunAsync(ProcessRunner.Shell(testCommand, ctx.WorkspaceRoot), TestTimeout, ct);
            yield return new ToolCallResult($"test-{tier.Id}", "tests", ProcessRunner.Format(test), test.ExitCode != 0);
            if (test.ExitCode == 0)
            {
                yield return new Done($"Solved by {tier.DisplayName}.");
                yield break;
            }
            previousFailure = test.Output;
        }
        yield return new ErrorEvent("No tier produced passing tests.");
    }
}
