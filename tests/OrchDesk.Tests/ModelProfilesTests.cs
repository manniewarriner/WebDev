using System.Text.Json;
using OrchDesk.Core.Config;
using OrchDesk.Core.Models;
using OrchDesk.Core.Providers;

namespace OrchDesk.Tests;

public class ModelProfilesTests
{
    [Theory]
    [InlineData("claude-sonnet-4-6", "Sonnet")]
    [InlineData("OPUS", "Opus")]
    [InlineData("haiku", "Haiku")]
    [InlineData("qwen/qwen3.5-9b", "Qwen")]
    [InlineData("google/gemma-4-12b-qat", "Gemma")]
    [InlineData("glm-5.2", "GLM")]
    [InlineData("glm-5.x", "GLM")]
    [InlineData("kimi-k2.5", "Kimi")]
    [InlineData("minimax-m2.5", "Minimax")]
    [InlineData("future-model", "Unknown")]
    [InlineData(null, "Unknown")]
    public void ResolvesFamiliesWithoutChangingRequestIds(string? id, string family) => Assert.Equal(family, ModelProfiles.Get(id).Family);

    [Fact]
    public void ProfilesHaveNormalizedMetricsAndLocalCostIsZero()
    {
        Assert.All(ModelProfiles.Table.Append(ModelProfiles.Unknown), p => {
            Assert.InRange(p.Speed, 0, 1); Assert.InRange(p.Cost, 0, 1); Assert.InRange(p.Context, 0, 1);
            Assert.False(string.IsNullOrWhiteSpace(p.ContextLabel));
        });
        Assert.Equal(0, ModelProfiles.Get("qwen3.5").Cost);
        Assert.Equal(0, ModelProfiles.Get("gemma4").Cost);
        Assert.True(ModelProfiles.Get("haiku").Speed > ModelProfiles.Get("opus").Speed);
        Assert.True(ModelProfiles.Get("opus").Cost > ModelProfiles.Get("sonnet").Cost);
    }

    [Theory]
    [InlineData(null)] [InlineData(true)] [InlineData(false)]
    public void MotionAndSidebarPreferencesRoundTrip(bool? reduce)
    {
        var config = new AppConfig { ReduceMotion = reduce, SidebarCollapsed = true };
        var restored = JsonSerializer.Deserialize<AppConfig>(JsonSerializer.Serialize(config, ConfigStore.Json), ConfigStore.Json)!;
        Assert.Equal(reduce, restored.ReduceMotion); Assert.True(restored.SidebarCollapsed);
        Assert.Null(new AppConfig().ReduceMotion);
    }

    [Fact]
    public async Task ReasoningDeltasRemainSeparateFromAnswer()
    {
        static async IAsyncEnumerable<string> Lines() {
            yield return """data: {"choices":[{"delta":{"reasoning_content":"Inspect "}}]}""";
            await Task.Yield();
            yield return """data: {"choices":[{"delta":{"reasoning_content":"the plan"}}]}""";
            yield return """data: {"choices":[{"delta":{"content":"Add an index"}}]}""";
            yield return "data: [DONE]";
        }
        var events = await SseChatParser.ParseAsync(Lines()).ToListAsync();
        Assert.Equal("Inspect the plan", string.Concat(events.OfType<ReasoningDelta>().Select(e => e.Text)));
        Assert.Equal("Add an index", Assert.Single(events.OfType<TextDelta>()).Text);
        Assert.Single(events.OfType<StatusUpdate>());
    }
}
