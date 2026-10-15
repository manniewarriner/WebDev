using OrchDesk.Core.Models;
using OrchDesk.Core.Providers;

namespace OrchDesk.Tests;

public sealed class ResponseUsageTests
{
    [Fact] public void Compatible_completion_usage() => Assert.Equal(new ResponseUsage(1200, 40), SseChatParser.ParseCompletion("""{"usage":{"prompt_tokens":1200,"completion_tokens":40},"choices":[{"message":{"content":"ok"}}]}""").OfType<UsageReported>().Single().Usage);
    [Fact] public async Task Compatible_stream_usage_without_choices()
    {
        static async IAsyncEnumerable<string> Lines() { yield return "data: {\"choices\":[],\"usage\":{\"prompt_tokens\":10,\"completion_tokens\":4}}"; await Task.CompletedTask; }
        var events = await SseChatParser.ParseAsync(Lines()).ToListAsync();
        Assert.Equal(new ResponseUsage(10, 4), events.OfType<UsageReported>().Single().Usage);
    }
    [Fact] public void Claude_result_usage() => Assert.Equal(new ResponseUsage(100, 20), ClaudeStreamParser.Parse("""{"type":"result","result":"ok","usage":{"input_tokens":100,"output_tokens":20}}""", "claude").Events.OfType<UsageReported>().Single().Usage);
    [Fact] public void OpenCode_usage() => Assert.Equal(new ResponseUsage(30, 12), OpenCodeStreamParser.Parse("""{"type":"step_finish","part":{"tokens":{"input":30,"output":12}}}""").OfType<UsageReported>().Single().Usage);
    [Fact] public void Formatting_and_estimate()
    {
        Assert.Equal("Thought for 14s · 1,240 tokens", new ResponseUsage(1200, 40).Format(TimeSpan.FromSeconds(14)));
        var estimate = ResponseUsage.Estimate("12345678", "1234");
        Assert.Equal("Thought for 2s · ~3 tokens", estimate.Format(TimeSpan.FromSeconds(2)));
        Assert.Contains("Input: 2", estimate.Tooltip("provider", "model"));
        Assert.Contains("provider / model", estimate.Tooltip("provider", "model"));
    }
}
