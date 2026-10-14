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
    public string Workspace { get; } = WorkspacePath.NormalizeRoot(workspace);

    /// <summary>null = Auto (follow the rotation chain). Otherwise try this provider first, then fall back.</summary>
    public string? PreferredProviderId { get; set; }
    public IModelProvider? LastProvider { get; private set; }

    public async IAsyncEnumerable<ChatEvent> SendAsync(string userText, [EnumeratorCancellation] CancellationToken ct, bool readOnly = false)
    {
        var userMessage = ChatMessage.User(userText);
        Messages.Add(userMessage);
        var ctx = new ToolContext(Workspace);
        var tried = new HashSet<string>();

        while (true)
        {
            var provider = PickProvider(tried);
            if (provider is null)
            {
                yield return new ErrorEvent(tried.Count == 0
                    ? "No model is available. Check the Accounts panel (PC offline, limits hit, or nothing configured)."
                    : "Every model in the chain is offline or out of quota right now.");
                yield break;
            }
            tried.Add(provider.Id);
            LastProvider = provider;
            yield return new StatusUpdate($"Using {provider.DisplayName}");

            ChatEvent? failover = null;
            await foreach (var ev in loop.RunAsync(provider, Messages, ctx, ct, readOnly))
            {
                if (ev is UsageLimitHit or ProviderUnavailable) { failover = ev; break; }
                yield return ev;
            }
            if (failover is null) yield break;

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
                yield return new StatusUpdate($"{provider.DisplayName} is offline — switching model. ({((ProviderUnavailable)failover).Message})");
            }
        }
    }

    private IModelProvider? PickProvider(HashSet<string> tried)
    {
        if (PreferredProviderId is { } id && !tried.Contains(id) && rotator.Find(id) is { } pref && rotator.IsUsable(pref))
            return pref.Provider;
        return rotator.Current(tried);
    }
}
