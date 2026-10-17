using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace OrchDesk.App.Services;

/// <summary>Imports baseline conversations once, leaving conversations.json intact for either build.</summary>
public static class LegacyChatImport
{
    private static readonly JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive = true };
    private sealed record Message(string Role, string Text);
    private sealed record Conversation(string Id, string Title, string Project, Message[] Messages);
    public static void Import(string directory)
    {
        var source = Path.Combine(directory, "conversations.json");
        if (!File.Exists(source)) return;
        var history = Path.Combine(directory, "history");
        var indexPath = Path.Combine(history, "legacy-imports.json.index");
        var imported = File.Exists(indexPath) ? JsonSerializer.Deserialize<HashSet<string>>(File.ReadAllText(indexPath)) ?? [] : [];
        var chats = JsonSerializer.Deserialize<Conversation[]>(File.ReadAllText(source), Json) ?? [];
        Directory.CreateDirectory(history);
        foreach (var chat in chats)
        {
            var id = "legacy-" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(chat.Id ?? chat.Title ?? "")))[..24];
            if (imported.Contains(id)) continue;
            var messages = (chat.Messages ?? []).Select(m => new SavedMessage(m.Role is "you" or "user" ? "user" : "assistant", m.Text ?? "", File.GetLastWriteTimeUtc(source))).ToArray();
            var target = Path.Combine(history, id + ".json");
            if (!File.Exists(target))
            {
                var saved = new SavedChat(chat.Title ?? "Imported chat", "", File.GetLastWriteTimeUtc(source), messages, chat.Project);
                File.WriteAllText(target + ".tmp", JsonSerializer.Serialize(saved)); File.Move(target + ".tmp", target);
            }
            imported.Add(id);
        }
        File.WriteAllText(indexPath + ".tmp", JsonSerializer.Serialize(imported)); File.Move(indexPath + ".tmp", indexPath, true);
    }
}
