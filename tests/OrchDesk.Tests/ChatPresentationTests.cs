using OrchDesk.App.ViewModels;
using OrchDesk.Core.Models;
using OrchDesk.Core.Providers;

namespace OrchDesk.Tests;

public class ChatPresentationTests
{
    [Fact]
    public void StreamingAnswerKeepsReasoningSeparate()
    {
        var answer = new AssistantItem("Sonnet") { IsStreaming = true, Reasoning = "Inspect the query" };
        answer.Append("Add "); answer.Append("an index.");
        Assert.Equal("Add an index.", answer.Text);
        Assert.Equal("Inspect the query", answer.Reasoning);
        answer.IsStreaming = false;
        Assert.False(answer.IsStreaming);
    }

    [Theory]
    [InlineData(false, "\u2713")]
    [InlineData(true, "\u2715")]
    public void ToolCompletionUpdatesStateAndDuration(bool error, string icon)
    {
        var tool = new ToolItem("one", "read_file", "{}");
        var changed = new List<string?>();
        tool.PropertyChanged += (_, e) => changed.Add(e.PropertyName);
        tool.IsError = error; tool.Output = "result"; tool.IsRunning = false;
        Assert.Equal(icon, tool.StateIcon);
        Assert.Contains(nameof(ToolItem.StateIcon), changed);
        Assert.Contains(nameof(ToolItem.Duration), changed);
        tool.ToggleCommand.Execute(null); Assert.True(tool.IsExpanded);
        tool.ToggleCommand.Execute(null); Assert.False(tool.IsExpanded);
        Assert.Equal("{}", tool.Arguments);
        Assert.Equal("result", tool.Output);
    }

    [Fact]
    public void ClaudeThinkingIsSeparateFromFinalText()
    {
        var result = ClaudeStreamParser.Parse("""{"type":"assistant","message":{"content":[{"type":"thinking","thinking":"Check the plan"},{"type":"text","text":"Done"}]}}""", "claude");
        Assert.Equal("Check the plan", Assert.Single(result.Events.OfType<ReasoningDelta>()).Text);
        Assert.Equal("Done", Assert.Single(result.Events.OfType<TextDelta>()).Text);
    }
}
