using OrchDesk.Core.Config;
using OrchDesk.Core.Automation;
using OrchDesk.Core.Knowledge;
using OrchDesk.Core.Telemetry;
using OrchDesk.Core.Dashboard;
using OrchDesk.Core.Research;
using OrchDesk.Core.Models;
using OrchDesk.Core.Tools;
using OrchDesk.Core.Permissions;
using OrchDesk.Core.Safety;
using OrchDesk.Core.Orchestration;
using OrchDesk.Core.Providers;
using Xunit;

namespace OrchDesk.Tests.Baseline;

public class ResearchTests
{
    [Fact]
    public void ModelsRankNewestProFirstAndDropNonChat()
    {
        var ranked = ResearchClient.Rank(["models/gemini-2.5-flash", "models/gemini-3.0-pro", "models/text-embedding-004", "gemini-2.5-pro", "models/gemini-3.0-flash-lite", "gemini-2.5-flash-image"]);
        Assert.Equal(["gemini-3.0-pro", "gemini-3.0-flash-lite", "gemini-2.5-pro", "gemini-2.5-flash"], ranked);
    }

    [Fact]
    public void RealGoogleListKeepsOnlyChatModelsAndDefaultsToNewestFlash()
    {
        var ranked = ResearchClient.Rank(["models/gemini-2.5-flash", "models/gemini-3.8-flash", "models/gemini-3.1-pro-preview", "models/gemini-flash-latest", "models/gemini-4-argon", "models/gemma-4-31b-it", "models/gemini-3.5-flash-lite", "models/gemini-robotics-er-2-preview", "models/gemini-3.1-pro-preview-customtools", "models/gemini-3.8-flash-tts", "models/lyria-3.5"]);
        Assert.Equal(["gemini-3.8-flash", "gemini-3.5-flash-lite", "gemini-3.1-pro-preview", "gemini-2.5-flash", "gemini-flash-latest"], ranked);
        Assert.Equal("gemini-3.8-flash", ResearchClient.Default(ranked));
        Assert.Null(ResearchClient.Default([]));
        Assert.Contains("retired", ResearchClient.Explain(new InvalidOperationException("Provider HTTP 404. Check settings and model ID.")));
    }

    [Fact]
    public void PromptCarriesTheNotebookAndCapsItsSize()
    {
        var big = new string('n', ResearchClient.MaxContextChars + 500);
        var messages = ResearchClient.Build("what is it?", "Field notes", big, [ChatMessage.User("earlier"), ChatMessage.Assistant("reply")]);
        Assert.Equal(ChatRole.System, messages[0].Role); Assert.Equal("earlier", messages[1].Content);
        var last = messages[^1].Content;
        Assert.Contains("Field notes", last); Assert.Contains("[source truncated]", last); Assert.EndsWith("what is it?", last);
        Assert.Equal("what is it?", ResearchClient.Build("what is it?", null, null, [])[^1].Content);
    }

    [Fact]
    public void ExistingConfigsGainTheGeminiEndpointOnce()
    {
        var config = new AppConfig { Endpoints = [new() { Name = "x", Url = "https://a.example/v1", KeyId = "x" }] };
        LegacyConfigImport.Apply(config, new System.Text.Json.Nodes.JsonObject()); LegacyConfigImport.Apply(config, new System.Text.Json.Nodes.JsonObject());
        Assert.Single(config.Endpoints, e => e.KeyId == "gemini");
        Assert.Equal(LegacyDefaults.GeminiUrl, config.Endpoints.Single(e => e.KeyId == "gemini").Url);
    }
}