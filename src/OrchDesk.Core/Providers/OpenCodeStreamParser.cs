using System.Text.Json.Nodes;
using OrchDesk.Core.Models;

namespace OrchDesk.Core.Providers;

public static class OpenCodeStreamParser
{
    public static IReadOnlyList<ChatEvent> Parse(string line)
    {
        JsonNode? node;
        try { node = JsonNode.Parse(line); } catch (System.Text.Json.JsonException) { return [new TextDelta(line + "\n")]; }
        var events = new List<ChatEvent>();
        if (node?["type"]?.ToString() == "text" && node["part"]?["text"] is { } text) events.Add(new TextDelta(text.ToString()));
        if (ResponseUsage.Parse(node?["usage"] ?? node?["part"]?["tokens"]) is { } usage) events.Add(new UsageReported(usage));
        if (node?["type"]?.ToString() == "error") events.Add(new ErrorEvent(node["error"]?["message"]?.ToString() ?? "OpenCode error"));
        return events;
    }
}
