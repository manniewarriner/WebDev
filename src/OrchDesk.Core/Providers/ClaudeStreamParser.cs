using System.Text.Json.Nodes;
using OrchDesk.Core.Models;

namespace OrchDesk.Core.Providers;

/// <summary>
/// Parses one line of `claude -p --output-format stream-json --verbose` output.
/// Shapes handled:
///   {"type":"system","subtype":"init","session_id":"…"}
///   {"type":"assistant","message":{"content":[{"type":"text","text":"…"},{"type":"tool_use","id","name","input"}]}}
///   {"type":"user","message":{"content":[{"type":"tool_result","tool_use_id","content","is_error"}]}}
///   {"type":"result","subtype":"success"|"error_…","is_error":bool,"result":"…","session_id":"…"}
/// </summary>
public static class ClaudeStreamParser
{
    private static readonly string[] LimitMarkers =
        ["usage limit", "limit reached", "rate limit", "rate_limit", "hit your limit", "429", "overloaded", "quota"];

    public sealed record ParseResult(IReadOnlyList<ChatEvent> Events, string? SessionId);

    public static bool LooksLikeUsageLimit(string? text) =>
        text is not null && LimitMarkers.Any(m => text.Contains(m, StringComparison.OrdinalIgnoreCase));

    public static ParseResult Parse(string line, string providerId)
    {
        var events = new List<ChatEvent>();
        if (line.Contains("Not logged in",StringComparison.OrdinalIgnoreCase) || line.Contains("Please run /login",StringComparison.OrdinalIgnoreCase))
            return new([new ProviderUnavailable(providerId,"Sign-in needed")],null);
        JsonNode? node;
        try { node = JsonNode.Parse(line); }
        catch (System.Text.Json.JsonException)
        {
            // Not JSON: the CLI printed a plain message (often an error). Surface it.
            if (LooksLikeUsageLimit(line)) events.Add(new UsageLimitHit(providerId, line));
            else if (!string.IsNullOrWhiteSpace(line)) events.Add(new StatusUpdate(line));
            return new ParseResult(events, null);
        }
        if (node is null) return new ParseResult(events, null);

        var type = node["type"]?.GetValue<string>();
        var sessionId = node["session_id"]?.GetValue<string>();

        switch (type)
        {
            // Newer CLI builds report limits as their own events before (or instead of) an error result.
            case "rate_limit_event":
                var status = node["rate_limit_info"]?["status"]?.GetValue<string>() ?? node["status"]?.GetValue<string>();
                if (string.Equals(status, "rejected", StringComparison.OrdinalIgnoreCase))
                {
                    var resets = node["rate_limit_info"]?["resetsAt"]?.ToString();
                    events.Add(new UsageLimitHit(providerId, resets is null ? "Claude usage limit reached." : $"Claude usage limit reached (resets {resets})."));
                }
                break;

            case "system" when node["subtype"]?.GetValue<string>() == "api_retry":
                events.Add(new StatusUpdate($"Claude API retry: {node["error"]?.ToString() ?? "unknown"}"));
                break;

            case "assistant":
                foreach (var block in node["message"]?["content"] as JsonArray ?? [])
                {
                    var blockType = block?["type"]?.GetValue<string>();
                    if (blockType == "text")
                        events.Add(new TextDelta(block!["text"]?.GetValue<string>() ?? ""));
                    else if (blockType == "thinking")
                        events.Add(new ReasoningDelta(block!["thinking"]?.GetValue<string>() ?? ""));
                    else if (blockType == "tool_use")
                        events.Add(new ToolCallRequested(new ToolCall(
                            block!["id"]?.GetValue<string>() ?? "",
                            block["name"]?.GetValue<string>() ?? "tool",
                            block["input"]?.ToJsonString() ?? "{}")));
                }
                break;

            case "user":
                foreach (var block in node["message"]?["content"] as JsonArray ?? [])
                {
                    if (block?["type"]?.GetValue<string>() != "tool_result") continue;
                    var content = block["content"] switch
                    {
                        JsonValue v when v.TryGetValue<string>(out var s) => s,
                        JsonArray arr => string.Join("\n", arr.Select(x => x?["text"]?.GetValue<string>()).Where(x => x is not null)),
                        var other => other?.ToJsonString() ?? "",
                    };
                    var isError = block["is_error"]?.GetValue<bool>() ?? false;
                    events.Add(new ToolCallResult(block["tool_use_id"]?.GetValue<string>() ?? "", "", content, isError));
                }
                break;

            case "result":
                if (ResponseUsage.Parse(node["usage"]) is { } usage) events.Add(new UsageReported(usage));
                var isErr = node["is_error"]?.GetValue<bool>() ?? false;
                var result = node["result"]?.GetValue<string>();
                if (isErr && LooksLikeUsageLimit(result ?? node["subtype"]?.GetValue<string>()))
                    events.Add(new UsageLimitHit(providerId, result ?? "Claude usage limit reached."));
                else if (isErr)
                    events.Add(new ErrorEvent(result ?? node["subtype"]?.GetValue<string>() ?? "Claude CLI error"));
                else
                    events.Add(new Done(result));
                break;
        }
        if (events.Any(ev => {
            var text=ev switch {TextDelta d=>d.Text, Done d=>d.FinalText, ErrorEvent e=>e.Message, StatusUpdate s=>s.Text, _=>null};
            return text is not null && (text.Contains("Not logged in",StringComparison.OrdinalIgnoreCase) || text.Contains("Please run /login",StringComparison.OrdinalIgnoreCase));
        })) return new([new ProviderUnavailable(providerId,"Sign-in needed")],sessionId);
        return new ParseResult(events, sessionId);
    }
}
