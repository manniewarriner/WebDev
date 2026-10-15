using OrchDesk.Core.Tools;
namespace OrchDesk.Core.Permissions;

public sealed class ChangeEntry(string workspace, string path, string action, byte[]? before, byte[]? after)
{
    public string Path { get; } = path;
    public string Action { get; } = action;
    public byte[]? Before { get; } = before;
    public byte[]? After { get; } = after;
    public bool Undone { get; private set; }
    public bool CanUndo => Action is "write_file" or "edit_file" or "delete_file" && !Undone;
    public string Diff => ChangeLog.Diff(Path, Before is null ? "" : System.Text.Encoding.UTF8.GetString(Before), After is null ? "" : System.Text.Encoding.UTF8.GetString(After));
    public void Undo()
    {
        if (!CanUndo) return;
        var target = WorkspacePath.Resolve(workspace, Path);
        var current = File.Exists(target) ? File.ReadAllBytes(target) : null;
        if (!(current is null && After is null) && !(current is not null && After is not null && current.SequenceEqual(After))) throw new IOException("File changed since this action. Undo newer changes first.");
        if (Before is null) File.Delete(target);
        else { Directory.CreateDirectory(System.IO.Path.GetDirectoryName(target)!); File.WriteAllBytes(target, Before); }
        Undone = true;
    }
}
public sealed class ChangeLog
{
    public List<ChangeEntry> Entries { get; } = [];
    public event Action<ChangeEntry>? Added;
    public void Record(ChangeEntry entry) { Entries.Add(entry); Added?.Invoke(entry); }
    public static string Diff(string path, string before, string after) => $"--- {path}\n+++ {path}\n" + string.Join("\n", before.Split('\n').Select(l => "-" + l)) + "\n" + string.Join("\n", after.Split('\n').Select(l => "+" + l));
}
