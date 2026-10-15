using System.Text.Json.Nodes;

namespace OrchDesk.Core.Models;

public enum ChatRole { System, User, Assistant, Tool }

/// <summary>A tool invocation requested by a model. Arguments are the raw JSON string the model produced.</summary>
public sealed record ToolCall(string Id, string Name, string ArgumentsJson);

/// <summary>One message in a conversation, in a provider-neutral shape.</summary>
public sealed class ChatMessage
{
    public ChatRole Role { get; init; }
    public string? Content { get; set; }
    public List<ToolCall>? ToolCalls { get; init; }
    public string? ToolCallId { get; init; }
    public string? ToolName { get; init; }

    public static ChatMessage System(string text) => new() { Role = ChatRole.System, Content = text };
    public static ChatMessage User(string text) => new() { Role = ChatRole.User, Content = text };
    public static ChatMessage Assistant(string? text, List<ToolCall>? calls = null) =>
        new() { Role = ChatRole.Assistant, Content = text, ToolCalls = calls is { Count: > 0 } ? calls : null };
    public static ChatMessage ToolResult(string callId, string name, string output) =>
        new() { Role = ChatRole.Tool, ToolCallId = callId, ToolName = name, Content = output };
}

/// <summary>A tool the model may call, described by a JSON Schema for its arguments.</summary>
public sealed record ToolDef(string Name, string Description, JsonObject Parameters);

/// <summary>Everything a provider needs for one turn.</summary>
public sealed record ChatRequest(
    IReadOnlyList<ChatMessage> Messages,
    IReadOnlyList<ToolDef> Tools,
    string? WorkingDirectory = null,
    bool ReadOnly = false,
    bool HostManagedTools = false);

/// <summary>Streamed output from a provider or the agent loop.</summary>
public abstract record ChatEvent
{
    public string? ExceptionType { get; init; }
    public int? HttpStatus { get; init; }
    public TimeSpan? RetryAfter { get; init; }
}
public sealed record StreamActivity : ChatEvent;
public sealed record ModelAttemptFailed(string Model, string Reason, long ElapsedMs) : ChatEvent;
public sealed record ReasoningDelta(string Text) : ChatEvent;
public sealed record TextDelta(string Text) : ChatEvent;
public sealed record ToolCallRequested(ToolCall Call) : ChatEvent;
public sealed record ToolCallResult(string CallId, string Name, string Output, bool IsError = false) : ChatEvent;
public sealed record UsageLimitHit(string ProviderId, string Message) : ChatEvent;
public sealed record ProviderUnavailable(string ProviderId, string Message) : ChatEvent;
public sealed record StatusUpdate(string Text) : ChatEvent;
public sealed record ErrorEvent(string Message) : ChatEvent;
public sealed record Done(string? FinalText = null) : ChatEvent;
