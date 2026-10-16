using System.Text.Json;
using OrchDesk.App.Services;
using OrchDesk.App.ViewModels;
using OrchDesk.Core.Config;
using OrchDesk.Core.Orchestration;
namespace OrchDesk.Tests;
public sealed class CaptureHistoryTests
{
    [Fact] public void CaptureParsesWithSend() {
        Assert.Equal("shots with spaces", SendOptions.Parse(["--send", "job", "--auto", "--capture", "shots with spaces"])!.CaptureDirectory);
        Assert.Null(SendOptions.Parse(["--send", "job"])!.CaptureDirectory);
    }
    [Theory]
    [InlineData("--capture", "shots")]
    [InlineData("--send", "job", "--capture")]
    [InlineData("--send", "job", "--capture", "--auto")]
    [InlineData("--send", "job", "--capture", " ")]
    [InlineData("--send", "job", "--capture", "a", "--capture", "b")]
    public void InvalidCaptureRejected(params string[] args) => Assert.Throws<ArgumentException>(() => SendOptions.Parse(args));

    [Fact] public void HistoryFileRoundTripPreservesTeamAndOldMessages() {
        var job = new TeamJob { PmProvider = "pm", Pm = "opus", Status = "failed" };
        job.Cards.Add(new() { Id = "worker", Title = "Build", Status = "failed", Output = "partial output", Model = "worker" });
        job.Failures.Add(new(DateTimeOffset.UtcNow, "worker", "worker", "timed out", "partial output"));
        job.Decisions.Add(new("retry", "worker", "backup", "timeout")); job.WorkerSkips.Add("offline model");
        var usage = new ModelUsageTracker(); var team = new TeamItem(); team.Refresh(job, usage);
        var saved = new SavedChat("job", "opus", DateTimeOffset.UtcNow, ChatHistory.SerializeItems([new UserItem("job"), team, new AssistantItem("opus") { Text = "Failed" }]));
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid()+".json");
        try {
            File.WriteAllText(path, JsonSerializer.Serialize(saved));
            var loaded = JsonSerializer.Deserialize<SavedChat>(File.ReadAllText(path))!;
            var items = ChatHistory.RestoreItems(loaded.Messages, loaded.Model, usage);
            Assert.Equal("job", Assert.IsType<UserItem>(items[0]).Text);
            var restored = Assert.IsType<TeamItem>(items[1]).Job!;
            Assert.Equal("failed", restored.Status); Assert.Equal("pm/opus", restored.Manager);
            Assert.Equal("partial output", Assert.Single(restored.Cards).Output);
            Assert.Equal("timed out", Assert.Single(restored.Failures).Reason);
            Assert.Equal("retry", Assert.Single(restored.Decisions).Decision);
            Assert.Equal("offline model", Assert.Single(restored.WorkerSkips));
            Assert.Equal("Failed", Assert.IsType<AssistantItem>(items[2]).Text);
            var legacy = JsonSerializer.Deserialize<SavedChat>("{\"Title\":\"old\",\"Model\":\"model\",\"Date\":\"2026-01-01T00:00:00Z\",\"Messages\":[{\"Role\":\"user\",\"Text\":\"hello\",\"Timestamp\":\"2026-01-01T00:00:00Z\"}]}")!;
            Assert.IsType<UserItem>(Assert.Single(ChatHistory.RestoreItems(legacy.Messages, legacy.Model, usage)));
        } finally { File.Delete(path); }
    }
}
