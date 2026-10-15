using OrchDesk.Core.Presentation;
namespace OrchDesk.Tests;
public sealed class ChatDeletionTests
{
    [Fact]
    public void Delete_removes_live_history_and_undo_restores_exact_contents()
    {
        var root = Path.Combine(Path.GetTempPath(), "orch-delete-" + Guid.NewGuid());
        Directory.CreateDirectory(root);
        try {
            var path = Path.Combine(root, "chat.json");
            File.WriteAllText(path, "{\"title\":\"test\",\"messages\":[\"hello\"]}");
            var original = File.ReadAllBytes(path);
            var deletion = new ChatDeletion(root);
            var trash = deletion.MoveToTrash("chat");
            Assert.Empty(Directory.GetFiles(root, "*.json"));
            Assert.False(File.Exists(path));
            Assert.True(File.Exists(trash));
            deletion.Restore(trash!);
            Assert.Equal(original, File.ReadAllBytes(path));
            Assert.False(File.Exists(trash));
        } finally { Directory.Delete(root, true); }
    }
    [Fact]
    public void Trash_is_retained_for_undo_and_purged_after_seven_days()
    {
        var root = Path.Combine(Path.GetTempPath(), "orch-purge-" + Guid.NewGuid());
        Directory.CreateDirectory(root);
        try {
            File.WriteAllText(Path.Combine(root, "chat.json"), "{}");
            var deletion = new ChatDeletion(root);
            var path = deletion.MoveToTrash("chat")!;
            deletion.PurgeExpired(DateTime.UtcNow);
            Assert.True(File.Exists(path));
            deletion.PurgeExpired(DateTime.UtcNow.AddDays(8));
            Assert.False(File.Exists(path));
        } finally { Directory.Delete(root, true); }
    }
    [Fact]
    public void Missing_file_is_safe_and_parent_traversal_is_rejected()
    {
        var deletion = new ChatDeletion(Path.GetTempPath());
        Assert.Null(deletion.MoveToTrash(Guid.NewGuid().ToString()));
        Assert.Throws<ArgumentException>(() => deletion.MoveToTrash("../other"));
    }
}
