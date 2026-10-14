using System.Text.Json.Nodes;

namespace OrchDesk.Core.Permissions;

public sealed record ApprovalRequest(string ToolName, string Summary, string Reason);

/// <summary>
/// Applies the policy and, for "Ask", awaits the user's answer. The UI supplies the approver
/// (it shows an Approve/Deny card and completes a TaskCompletionSource when a button is clicked).
/// "Always allow" answers are remembered for the rest of the session.
/// </summary>
public sealed class PermissionGate(PermissionPolicy policy, Func<ApprovalRequest, CancellationToken, Task<ApprovalAnswer>>? approver)
{
    private readonly HashSet<string> _sessionAllowed = new(StringComparer.Ordinal);

    public PermissionPolicy Policy => policy;

    public async Task<PermissionDecision> CheckAsync(string toolName, JsonObject args, CancellationToken ct)
    {
        var decision = policy.Evaluate(toolName, args);
        if (decision.Verdict != Verdict.Ask) return decision;

        var summary = Describe(toolName, args);
        if (_sessionAllowed.Contains(summary)) return PermissionDecision.Allow("approved earlier this session");
        if (approver is null) return PermissionDecision.Deny($"{decision.Reason}: no one available to approve");

        var answer = await approver(new ApprovalRequest(toolName, summary, decision.Reason), ct);
        if (answer == ApprovalAnswer.AlwaysThisSession) _sessionAllowed.Add(summary);
        return answer == ApprovalAnswer.Deny
            ? PermissionDecision.Deny("The user denied this action.")
            : PermissionDecision.Allow("approved by user");
    }

    public static string Describe(string toolName, JsonObject args) => toolName switch
    {
        "run_command" => $"Run: {args["command"]}",
        "delete_file" => $"Delete file: {args["path"]}",
        "git_push" => "git push -u origin HEAD",
        _ => $"{toolName} {args.ToJsonString()}",
    };
}

public enum ApprovalAnswer { Deny, Approve, AlwaysThisSession }
