using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json.Nodes;
using OrchDesk.Core.Models;

namespace OrchDesk.Core.Providers;

/// <summary>
/// Turns the Server-Sent Events stream of an OpenAI-compatible /chat/completions call into ChatEvents.
/// Text arrives as many small "delta.content" pieces; tool calls arrive as fragments keyed by index
/// (id and name first, then the JSON arguments in chunks), so they are assembled and emitted at the end.
/// </summary>
public static class SseChatParser
{
    public static IEnumerable<ChatEvent> ParseCompletion(string json)
    {
        var node = JsonNode.Parse(json);
        if (node?["error"] is { } error)
        {
            yield return new ErrorEvent(error["message"]?.ToString() ?? "Provider error");
            yield break;
        }
        var message = (node?["choices"] as JsonArray)?.FirstOrDefault()?["message"];
        if (message is null) { yield return new ErrorEvent("Missing completion message."); yield break; }
        if (message["reasoning_content"] is JsonValue reasoning && reasoning.TryGetValue<string>(out var thought) && thought.Length > 0)
            yield return new ReasoningDelta(thought);
        if (message["content"] is JsonValue value && value.TryGetValue<string>(out var text) && text.Length > 0)
            yield return new TextDelta(text);
        if (message["tool_calls"] is JsonArray calls)
            foreach (var call in calls.OfType<JsonObject>())
                yield return new ToolCallRequested(new ToolCall(call["id"]?.ToString() ?? Guid.NewGuid().ToString("N"),
                    call["function"]?["name"]?.ToString() ?? "", call["function"]?["arguments"]?.ToString() ?? "{}"));
        yield return new Done();
    }

    public static async IAsyncEnumerable<ChatEvent> ParseAsync(
        IAsyncEnumerable<string> lines, [EnumeratorCancellation] CancellationToken ct = default)
    {
        var thinkingShown = false;
        var calls = new SortedDictionary<int, (string Id, string Name, StringBuilder Args)>();

        await foreach (var raw in lines.WithCancellation(ct))
        {
            var line = raw.Trim();
            if (!line.StartsWith("data:", StringComparison.Ordinal)) continue;
            var data = line[5..].Trim();
            if (data == "[DONE]") break;

            JsonNode? node;
            try { node = JsonNode.Parse(data); }
            catch (System.Text.Json.JsonException) { continue; }

            if (node?["error"] is JsonNode err)
            {
                yield return new ErrorEvent(err["message"]?.GetValue<string>() ?? err.ToJsonString());
                yield break;
            }

            var delta = (node?["choices"] as JsonArray)?.FirstOrDefault()?["delta"];
            if (delta is null) continue;

            // Reasoning is separate from the answer; never mix thinking into tool arguments or transcript.
            if (delta["reasoning_content"] is JsonValue reasoning && reasoning.TryGetValue<string>(out var thought) && thought.Length > 0)
            {
                if (!thinkingShown) { thinkingShown = true; yield return new StatusUpdate("Thinking..."); }
                yield return new ReasoningDelta(thought);
            }
            if (delta["content"] is JsonValue content && content.TryGetValue<string>(out var text) && text.Length > 0)
                yield return new TextDelta(text);

            if (delta["tool_calls"] is JsonArray toolCalls)
            {
                foreach (var tc in toolCalls)
                {
                    if (tc is null) continue;
                    var index = tc["index"]?.GetValue<int>() ?? 0;
                    if (!calls.TryGetValue(index, out var acc))
                        acc = (string.Empty, string.Empty, new StringBuilder());
                    var id = tc["id"]?.GetValue<string>();
                    var name = tc["function"]?["name"]?.GetValue<string>();
                    var args = tc["function"]?["arguments"]?.GetValue<string>();
                    if (!string.IsNullOrEmpty(id)) acc.Id = id;
                    if (!string.IsNullOrEmpty(name)) acc.Name += name;
                    if (args is not null) acc.Args.Append(args);
                    calls[index] = acc;
                }
            }
        }

        foreach (var (index, acc) in calls)
        {
            var id = string.IsNullOrEmpty(acc.Id) ? $"call_{index}" : acc.Id;
            var args = acc.Args.Length == 0 ? "{}" : acc.Args.ToString();
            yield return new ToolCallRequested(new ToolCall(id, acc.Name, args));
        }
        yield return new Done();
    }
}
