namespace OrchDesk.Core.Presentation;

/// <summary>Moves chat history out of the live store before offering undo.</summary>
public sealed class ChatDeletion(string directory)
{
    public string? MoveToTrash(string id)
    {
        if (Path.GetFileName(id) != id) throw new ArgumentException("Invalid chat id", nameof(id));
        var source = Path.Combine(directory, id + ".json");
        if (!File.Exists(source)) return null;
        var trash = Path.Combine(directory, ".trash");
        Directory.CreateDirectory(trash);
        var destination = Path.Combine(trash, id + ".json");
        File.Move(source, destination, true);
        File.SetLastWriteTimeUtc(destination, DateTime.UtcNow);
        return destination;
    }
    public void PurgeExpired(DateTime utcNow)
    {
        var trash = Path.Combine(directory, ".trash");
        if (!Directory.Exists(trash)) return;
        foreach (var path in Directory.EnumerateFiles(trash, "*.json"))
            if (File.GetLastWriteTimeUtc(path) < utcNow.AddDays(-7)) File.Delete(path);
    }
    public void Restore(string trashPath) => File.Move(trashPath, Path.Combine(directory, Path.GetFileName(trashPath)), true);
}
