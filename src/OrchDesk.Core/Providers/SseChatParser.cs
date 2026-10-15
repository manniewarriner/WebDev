using System.Runtime.CompilerServices;
using System.Text;
using System.Text.RegularExpressions;
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
            foreach (var ev in ParseContent(text, message["tool_calls"] is not JsonArray { Count: > 0 })) yield return ev;
        if (message["tool_calls"] is JsonArray calls)
            foreach (var call in calls.OfType<JsonObject>())
                yield return new ToolCallRequested(new ToolCall(call["id"]?.ToString() ?? Guid.NewGuid().ToString("N"),
                    call["function"]?["name"]?.ToString() ?? "", call["function"]?["arguments"]?.ToString() ?? "{}"));
        yield return new Done();
    }

    public static IEnumerable<ChatEvent> ParseContent(string text, bool allowTools = true)
    {
        var thoughts = Regex.Matches(text, @"<think(?:ing)?>(.*?)</think(?:ing)?>", RegexOptions.Singleline | RegexOptions.IgnoreCase);
        foreach (Match thought in thoughts) yield return new ReasoningDelta(thought.Groups[1].Value);
        text = Regex.Replace(text, @"<think(?:ing)?>.*?</think(?:ing)?>", "", RegexOptions.Singleline | RegexOptions.IgnoreCase);
        foreach (Match match in Regex.Matches(text, @"<tool_call>(.*?)</tool_call>", RegexOptions.Singleline | RegexOptions.IgnoreCase))
        {
            ToolCall? call = null;
            if (allowTools)
            {
                try
                {
                    var node = JsonNode.Parse(match.Groups[1].Value.Trim());
                    var function = node?["function"] ?? node;
                    if (function?["name"]?.GetValue<string>() is { Length: > 0 } name)
                    {
                        var args = function["arguments"];
                        var json = args is JsonValue value && value.TryGetValue<string>(out var raw) ? raw : args?.ToJsonString() ?? "{}";
                        JsonNode.Parse(json); // malformed arguments must not execute
                        call = new(node?["id"]?.ToString() ?? Guid.NewGuid().ToString("N"), name, json);
                    }
                }
                catch (System.Text.Json.JsonException) { }
                catch (InvalidOperationException) { }
            }
            if (call is not null) yield return new ToolCallRequested(call);
        }
        text = Regex.Replace(text, @"<tool_call>.*?</tool_call>", "", RegexOptions.Singleline | RegexOptions.IgnoreCase);
        if (!string.IsNullOrWhiteSpace(text)) yield return new TextDelta(text);
    }

    public static async IAsyncEnumerable<ChatEvent> ParseAsync(
        IAsyncEnumerable<string> lines, [EnumeratorCancellation] CancellationToken ct = default)
    {
        var thinkingShown = false;
        var contentText = new StringBuilder();
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
            {
                // Preserve ordinary streaming; buffer markup from its first fragment so split
                // reasoning/tool tags never leak into the answer or get executed twice.
                if (contentText.Length == 0 && !text.Contains('<')) yield return new TextDelta(text);
                else contentText.Append(text);
            }

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

        foreach (var ev in ParseContent(contentText.ToString(), calls.Count == 0)) yield return ev;

        foreach (var (index, acc) in calls)
        {
            var id = string.IsNullOrEmpty(acc.Id) ? $"call_{index}" : acc.Id;
            var args = acc.Args.Length == 0 ? "{}" : acc.Args.ToString();
            yield return new ToolCallRequested(new ToolCall(id, acc.Name, args));
        }
        yield return new Done();
    }
}
