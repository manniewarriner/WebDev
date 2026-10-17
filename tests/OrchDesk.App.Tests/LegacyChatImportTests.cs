using System.Text.Json;
using OrchDesk.App.Services;

namespace OrchDesk.Tests;

public sealed class LegacyChatImportTests
{
    [Fact]
    public void ImportsEveryModelLabelOnceAndDoesNotResurrectDeletedChats()
    {
        var directory = Path.Combine(Path.GetTempPath(), "OrchDesk-import-" + Guid.NewGuid()); Directory.CreateDirectory(directory);
        try
        {
            var source = Path.Combine(directory, "conversations.json");
            const string json = """[{"id":"existing","title":"Legacy chat","project":"C:\\project","messages":[{"role":"you","text":"Hello"},{"role":"gemini-unfamiliar","text":"Answer"}]}]""";
            File.WriteAllText(source, json); LegacyChatImport.Import(directory); LegacyChatImport.Import(directory);
            var path = Assert.Single(Directory.GetFiles(Path.Combine(directory, "history"), "*.json"));
            var saved = JsonSerializer.Deserialize<SavedChat>(File.ReadAllText(path))!;
            Assert.Equal(new[] { "Hello", "Answer" }, saved.Messages.Select(m => m.Text)); Assert.Equal("C:\\project", saved.Workspace);
            Assert.Equal(json, File.ReadAllText(source));
            File.Delete(path); LegacyChatImport.Import(directory); Assert.Empty(Directory.GetFiles(Path.Combine(directory, "history"), "*.json"));
        }
        finally { Directory.Delete(directory, true); }
    }
}
