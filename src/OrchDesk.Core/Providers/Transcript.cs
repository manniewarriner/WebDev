using System.Text;
using OrchDesk.Core.Models;

namespace OrchDesk.Core.Providers;

/// <summary>Turns a structured conversation into one prompt string for CLI agents that take plain text.</summary>
public static class Transcript
{
    public static string LastUserMessage(IReadOnlyList<ChatMessage> messages) =>
        messages.LastOrDefault(m => m.Role == ChatRole.User)?.Content ?? string.Empty;

    public static string Flatten(IReadOnlyList<ChatMessage> messages, int maxToolOutputChars = 2000)
    {
        var userTurns = messages.Count(m => m.Role == ChatRole.User);
        if (userTurns <= 1 && messages.All(m => m.Role is ChatRole.User or ChatRole.System))
            return string.Join("\n\n", messages.Select(m => m.Content));

        var sb = new StringBuilder("Conversation so far (continue from the last user message):\n\n");
        foreach (var m in messages)
        {
            switch (m.Role)
            {
                case ChatRole.System: sb.Append("[system]\n").Append(m.Content).Append("\n\n"); break;
                case ChatRole.User: sb.Append("[user]\n").Append(m.Content).Append("\n\n"); break;
                case ChatRole.Assistant:
                    if (!string.IsNullOrWhiteSpace(m.Content)) sb.Append("[assistant]\n").Append(m.Content).Append("\n\n");
                    foreach (var c in m.ToolCalls ?? []) sb.Append($"[assistant called {c.Name} {c.ArgumentsJson}]\n");
                    break;
                case ChatRole.Tool:
                    var output = m.Content ?? "";
                    if (output.Length > maxToolOutputChars) output = output[..maxToolOutputChars] + "\n…(truncated)";
                    sb.Append($"[result of {m.ToolName}]\n").Append(output).Append("\n\n");
                    break;
            }
        }
        return sb.ToString();
    }
}
