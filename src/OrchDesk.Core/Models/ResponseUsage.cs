using System.Globalization;
using System.Text.Json.Nodes;

namespace OrchDesk.Core.Models;

public sealed record ResponseUsage(long InputTokens, long OutputTokens, bool Estimated = false)
{
    public string Format(TimeSpan elapsed) => $"Thought for {elapsed.TotalSeconds:0}s · {(Estimated ? "~" : "")}{(InputTokens + OutputTokens).ToString("N0", CultureInfo.InvariantCulture)} tokens";
    public string Tooltip(string provider, string model) => $"{provider} / {model}\nInput: {InputTokens:N0} tokens\nOutput: {OutputTokens:N0} tokens{(Estimated ? " (estimated)" : "")}";
    public static ResponseUsage Estimate(string input, string output) => new((input.Length + 3) / 4, (output.Length + 3) / 4, true);
    public static ResponseUsage? Parse(JsonNode? usage)
    {
        if (usage is not JsonObject obj) return null;
        var input = obj["prompt_tokens"] ?? obj["input_tokens"] ?? obj["input"];
        var output = obj["completion_tokens"] ?? obj["output_tokens"] ?? obj["output"];
        return input is null || output is null ? null : new(input.GetValue<long>(), output.GetValue<long>());
    }
}
public sealed record UsageReported(ResponseUsage Usage) : ChatEvent;
