using OrchDesk.Core.Models;

namespace OrchDesk.Core.Providers;

public enum ProviderHealth { Unknown, Online, Offline, NotConfigured }

public interface IModelProvider
{
    string Id { get; }
    string DisplayName { get; }

    /// <summary>
    /// True when the provider runs its own agent (Claude/Codex/OpenCode CLIs) and executes tools itself.
    /// False means OrchDesk's <see cref="Agent.AgentLoop"/> must execute tool calls.
    /// </summary>
    bool IsAgentic { get; }

    /// <summary>True for models served from the user's own PC (used to prefer/skip them when offline).</summary>
    bool IsLocal { get; }

    IAsyncEnumerable<ChatEvent> StreamChatAsync(ChatRequest request, CancellationToken ct);

    /// <summary>Cheap reachability check (e.g. GET /models, or CLI on PATH). Must not throw.</summary>
    Task<ProviderHealth> CheckHealthAsync(CancellationToken ct);
}
