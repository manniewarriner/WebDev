using System.Text.Json;
using OrchDesk.Core.Models;
using OrchDesk.Core.Providers;
using Xunit;
namespace OrchDesk.Tests;
public sealed class QwenParserTests
{
    [Theory]
    [InlineData("<tool_call>{\"name\":\"write_file\",\"arguments\":{\"path\":\"smoke.txt\",\"content\":\"ORCHDESK_OK\"}}</tool_call>")]
    [InlineData("<think>Use the tool</think><tool_call>{\"function\":{\"name\":\"write_file\",\"arguments\":\"{\\\"path\\\":\\\"smoke.txt\\\",\\\"content\\\":\\\"ORCHDESK_OK\\\"}\"}}</tool_call>")]
    public async Task ContentToolCallsParseInCompletionAndFragmentedStream(string content)
    {
        var completion = JsonSerializer.Serialize(new { choices = new[] { new { message = new { content } } } });
        var call = Assert.Single(SseChatParser.ParseCompletion(completion).OfType<ToolCallRequested>());
        Assert.Equal("write_file", call.Call.Name);
        Assert.Contains("ORCHDESK_OK", call.Call.ArgumentsJson);
        var events = new List<ChatEvent>();
        await foreach (var ev in SseChatParser.ParseAsync(Lines(content))) events.Add(ev);
        Assert.Equal("write_file", Assert.Single(events.OfType<ToolCallRequested>()).Call.Name);
        Assert.DoesNotContain(events.OfType<TextDelta>(), t => t.Text.Contains("tool_call") || t.Text.Contains("<think>"));
    }
    private static async IAsyncEnumerable<string> Lines(string content)
    {
        foreach (var ch in content) { yield return "data: " + JsonSerializer.Serialize(new { choices = new[] { new { delta = new { content = ch.ToString() } } } }); }
        yield return "data: [DONE]";
        await Task.CompletedTask;
    }
}
