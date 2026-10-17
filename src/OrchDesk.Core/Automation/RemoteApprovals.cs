using System.Collections.Concurrent;
using OrchDesk.Core.Permissions;

namespace OrchDesk.Core.Automation;

/// <summary>
/// Approvals asked over Telegram. Each request gets a number, works once, and is denied automatically after <see cref="Timeout"/>.
/// Without a number, /approve only applies when exactly one request is waiting.
/// </summary>
public sealed class RemoteApprovals
{
    private readonly ConcurrentDictionary<int, TaskCompletionSource<bool>> pending = new();
    private int counter;
    public TimeSpan Timeout { get; init; } = TimeSpan.FromMinutes(5);
    public int Waiting => pending.Count;

    /// <summary>Sends the request through <paramref name="send"/> and waits for /approve or /deny. Requests the chat may not approve are denied straight away.</summary>
    public async Task<bool> AskAsync(ApprovalRequest request, Func<string, CancellationToken, Task> send, CancellationToken ct)
    {
        if (!RemotePolicy.CanApproveRemotely(request))
        {
            await send($"Denied: {request.Summary} can only be approved at the desktop ({request.Reason})", ct);
            return false;
        }
        int id = Interlocked.Increment(ref counter);
        var completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously); pending[id] = completion;
        try
        {
            await send($"Approval #{id}: {request.Summary}\nWhy: {request.Reason}\nReply /approve {id} or /deny {id}. Valid for this one action only, and denied automatically in {Timeout.TotalMinutes:0} minutes.", ct);
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct); timeout.CancelAfter(Timeout);
            using var _ = timeout.Token.Register(() => completion.TrySetResult(false));
            return await completion.Task;
        }
        finally { pending.TryRemove(id, out _); }
    }

    public string Resolve(bool approve, string? id)
    {
        var waiting = pending.ToArray();
        if (waiting.Length == 0) return "Nothing is waiting for approval.";
        KeyValuePair<int, TaskCompletionSource<bool>> target;
        if (id == null) { if (waiting.Length > 1) return "Several approvals are waiting. Reply with the number, for example /approve " + waiting.Min(w => w.Key) + "."; target = waiting[0]; }
        else if (!int.TryParse(id, out var n) || !pending.TryGetValue(n, out var found)) return "No pending approval with that number. It may have expired.";
        else target = new(n, found);
        return target.Value.TrySetResult(approve) ? (approve ? $"Approved #{target.Key}." : $"Denied #{target.Key}.") : "That approval already expired.";
    }

    public void DenyAll() { foreach (var p in pending.Values) p.TrySetResult(false); pending.Clear(); }
}
