using System.Runtime.CompilerServices;
using OrchDesk.Core.Accounts;
using OrchDesk.Core.Agent;
using OrchDesk.Core.Models;
using OrchDesk.Core.Providers;
using OrchDesk.Core.Tools;

namespace OrchDesk.Core.Orchestration;

/// <summary>
/// One chat in one folder. Sends each user message to a model and, if that model is offline (PC switched off)
/// or out of quota, rolls the conversation back to before the attempt and retries on the next model in the chain.
/// </summary>
public sealed class Conversation(AgentLoop loop, AccountRotator rotator, string workspace)
{
    public List<ChatMessage> Messages { get; } = [];
    public OrchDesk.Core.Permissions.ChangeLog Changes { get; } = loop.Changes ??= new();
    public string Workspace { get; } = WorkspacePath.NormalizeRoot(workspace);

    /// <summary>null = Auto (follow the rotation chain). Otherwise try this provider first, then fall back.</summary>
    public string? PreferredProviderId { get; set; }
    public TimeSpan? FirstTokenTimeout { get; set; }
    public string? StickyProviderId { get; set; }
    public List<ProviderAttempt> Attempts { get; } = [];
    public IModelProvider? LastProvider { get; private set; }

    public async IAsyncEnumerable<ChatEvent> SendAsync(string userText, [EnumeratorCancellation] CancellationToken ct, bool readOnly = false)
    {
        ct.ThrowIfCancellationRequested();
        Attempts.Clear();
        var userMessage = ChatMessage.User(userText);
        Messages.Add(userMessage);
        var ctx = new ToolContext(Workspace);
        var tried = new HashSet<string>();

        while (true)
        {
            var provider = PickProvider(tried);
            if (provider is null)
            {
                foreach (var account in rotator.Accounts.Where(a => !tried.Contains(a.Provider.Id)))
                    Attempts.Add(new(account.Provider.Id, account.Provider.ModelId,
                        account.Health == ProviderHealth.NotConfigured ? "not logged in / not configured" : account.LastLimitHit is not null ? "limit" : "offline"));
                yield return new ErrorEvent("All providers unavailable: " + (Attempts.Count == 0 ? "none configured" : string.Join("; ", Attempts.Select(a => $"{a.ProviderId}: {a.Reason}"))));
                yield break;
            }
            tried.Add(provider.Id);
            LastProvider = provider;
            var watch = System.Diagnostics.Stopwatch.StartNew();
            yield return new StatusUpdate($"Using {provider.DisplayName}");

            ChatEvent? failover = null;
            var answered = false;
            await foreach (var ev in GuardedRun(provider, ctx, ct, readOnly))
            {
                if (ev is ModelAttemptFailed modelFailure)
                {
                    Attempts.Add(new(provider.Id, modelFailure.Model, modelFailure.Reason, modelFailure.ExceptionType, modelFailure.HttpStatus, modelFailure.ElapsedMs));
                    continue;
                }
                if (ev is UsageLimitHit or ProviderUnavailable or ErrorEvent) { failover = ev; break; }
                if (ev is TextDelta { Text.Length: > 0 } or ToolCallRequested or Done { FinalText.Length: > 0 }) answered = true;
                yield return ev;
            }
            if (failover is null && !answered) failover = new ProviderUnavailable(provider.Id, "Empty response");
            if (failover is null) { StickyProviderId = provider.Id; Attempts.Add(new(provider.Id, provider.ModelId, "answered", ElapsedMs: watch.ElapsedMilliseconds)); yield break; }
            Attempts.Add(new(provider.Id, provider.ModelId, failover is UsageLimitHit ? "limit" : failover is ErrorEvent error ? (error.Message.Contains("401") || error.Message.Contains("403") || error.Message.Contains("log", StringComparison.OrdinalIgnoreCase) ? "not logged in" : "error") : "offline / timeout", failover.ExceptionType, failover.HttpStatus, watch.ElapsedMilliseconds));

            // Undo the partial attempt so the next model starts from the user's message. (Found by reference, not by
            // count: the loop may have inserted a system prompt at the front.)
            var keep = Messages.IndexOf(userMessage) + 1;
            Messages.RemoveRange(keep, Messages.Count - keep);
            if (failover is UsageLimitHit limit)
            {
                rotator.MarkLimitHit(provider.Id, limit.Message);
                yield return new StatusUpdate($"{provider.DisplayName} hit its usage limit — switching model.");
            }
            else
            {
                rotator.MarkOffline(provider.Id);
                yield return new StatusUpdate($"{provider.DisplayName} is offline — switching model. ({Attempts[^1].Reason})");
            }
        }
    }

    // Bound waiting for provider events independently from a long-running reply/tool execution.
    private async IAsyncEnumerable<ChatEvent> GuardedRun(IModelProvider provider, ToolContext ctx,
        [EnumeratorCancellation] CancellationToken ct, bool readOnly)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        await using var events = loop.RunAsync(provider, Messages, ctx, timeout.Token, readOnly).GetAsyncEnumerator(timeout.Token);
        var first = true;
        var firstDeadline = DateTimeOffset.UtcNow + (FirstTokenTimeout ?? TimeSpan.FromSeconds(20));
        while (true)
        {
            ChatEvent? failure = null;
            bool more = false;
            try
            {
                var wait = first ? firstDeadline - DateTimeOffset.UtcNow : TimeSpan.FromMinutes(5);
                if (wait <= TimeSpan.Zero) throw new OperationCanceledException();
                timeout.CancelAfter(wait);
                more = await events.MoveNextAsync();
            }
            catch (OperationCanceledException ex) when (!ct.IsCancellationRequested) { failure = new ProviderUnavailable(provider.Id, "Response timed out") { ExceptionType = ex.GetType().Name }; }
            catch (Exception ex) when (!ct.IsCancellationRequested) { failure = new ProviderUnavailable(provider.Id, "Provider failed") { ExceptionType = ex.GetType().Name, HttpStatus = ex is HttpRequestException http ? (int?)http.StatusCode : null }; }
            if (failure is not null) { yield return failure; yield break; }
            if (!more) yield break;
            // Any provider event proves the stream is alive, including reasoning and keepalives.
            first = false;
            timeout.CancelAfter(Timeout.InfiniteTimeSpan);
            yield return events.Current;
        }
    }

    private IModelProvider? PickProvider(HashSet<string> tried)
    {
        if ((PreferredProviderId ?? StickyProviderId) is { } id && !tried.Contains(id) && rotator.Find(id) is { } pref && rotator.IsUsable(pref, forSend: true))
            return pref.Provider;
        return rotator.Current(tried, forSend: true);
    }
}

public sealed record ProviderAttempt(string ProviderId, string Model, string Reason, string? ExceptionType = null, int? HttpStatus = null, long ElapsedMs = 0);
