using System.Text.Json.Nodes;
using OrchDesk.Core.Models;

namespace OrchDesk.Core.Providers;

public static class OpenCodeStreamParser
{
    /// <summary>
    /// Parses one `opencode run --format json` line (1.x and 2.x). step_start / tool_use / step_finish without usage
    /// produce no events; V2 `error` lines map 401/403 provider.auth to ProviderUnavailable and 429 / limit text to UsageLimitHit.
    /// </summary>
    public static IReadOnlyList<ChatEvent> Parse(string line, string providerId = "opencode-cli")
    {
        JsonNode? node;
        try { node = JsonNode.Parse(line); } catch (System.Text.Json.JsonException) { return [new TextDelta(line + "\n")]; }
        var events = new List<ChatEvent>();
        var type = node?["type"]?.ToString();
        if (type == "text" && node?["part"]?["text"] is { } text) events.Add(new TextDelta(text.ToString()));
        if (ResponseUsage.Parse(node?["usage"] ?? node?["part"]?["tokens"]) is { } usage) events.Add(new UsageReported(usage));
        if (type == "error") events.Add(ParseError(node!["error"], providerId));
        return events;
    }

    private static ChatEvent ParseError(JsonNode? error, string providerId)
    {
        var message = error?["message"]?.ToString() ?? error?["data"]?["message"]?.ToString() ?? "OpenCode error";
        var kind = error?["type"]?.ToString() ?? error?["name"]?.ToString() ?? "";
        var status = 0;
        try { status = error?["status"]?.GetValue<int>() ?? error?["response"]?["status"]?.GetValue<int>() ?? 0; } catch (Exception) { }
        if (status == 429 || ClaudeStreamParser.LooksLikeUsageLimit(message)) return new UsageLimitHit(providerId, message);
        if (status is 401 or 403 || kind.Contains("auth", StringComparison.OrdinalIgnoreCase))
            return new ProviderUnavailable(providerId, status == 0 ? $"OpenCode sign-in needed: {message}" : $"OpenCode sign-in or access needed ({status}): {message}");
        return new ErrorEvent(message);
    }
}
