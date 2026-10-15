using OrchDesk.Core.Config;
using OrchDesk.App.ViewModels;
namespace OrchDesk.Tests;

public class ProviderPresentationTests
{
    [Theory]
    [InlineData("sonnet", "Claude Sonnet")]
    [InlineData("claude-opus-4-6", "Claude Opus")]
    [InlineData("haiku", "Claude Haiku")]
    [InlineData("qwen/qwen3.5-9b", "Qwen 3.5 9B")]
    [InlineData("google/gemma-4-12b-qat", "Gemma 4 12B")]
    [InlineData("gemma-4-e2b", "Gemma 4 E2B")]
    [InlineData("bonsai-27b", "Bonsai 27B")]
    [InlineData("nemotron-3-nano-4b", "Nemotron 3 Nano 4B")]
    [InlineData("glm-5.2", "GLM 5.2")]
    [InlineData("glm-5.x", "GLM 5.x")]
    [InlineData("some_model-name", "Some Model Name")]
    public void NamesAreFriendly(string id, string expected) => Assert.Equal(expected, ModelDisplayNames.Get(id));

    [Theory]
    [InlineData("glm-5.2")]
    [InlineData(null)]
    public void IntegrationTitleUsesProviderDisplayName(string? model)
    {
        var row = new ProviderEdit(new() { Id = "opencode-go", DisplayName = "OpenCode Go", Model = model });
        Assert.Equal("OpenCode Go", row.FriendlyName);
        row.Model = "glm-5.x";
        Assert.Equal("OpenCode Go", row.FriendlyName);
        row.DisplayName = "Renamed provider";
        Assert.Equal("Renamed provider", row.FriendlyName);
    }

    [Fact]
    public void PickerKeepsRequestIds()
    {
        var options = ModelPickerOption.Expand([new() { Id = "local-pc", AvailableModels = ["qwen/qwen3.5-9b"] }]);
        Assert.Equal("qwen/qwen3.5-9b", options[0].Model);
        Assert.Equal("Qwen 3.5 9B", options[0].ModelName);
    }

    [Fact]
    public void ConnectionRequiresEnabledAndSynced()
    {
        var row = new ProviderEdit(new() { Enabled = true });
        Assert.False(row.IsConnected);
        row.ModelsSyncedAt = DateTimeOffset.Now; row.RefreshConnection();
        Assert.True(row.IsConnected);
        row.ManageCommand.Execute(null);
        Assert.True(row.DetailsExpanded);
        row.Enabled = false;
        Assert.False(row.IsConnected);
    }

    [Theory]
    [InlineData("opencode-go", true, false)]
    [InlineData("opencode-go", false, true)]
    [InlineData("claude-gmail", true, true)]
    public void DisconnectClearsModelsAndOnlyRemovesOpenCodeKeyWhenRequested(string id, bool removeKey, bool keepsKey)
    {
        var original = new ProviderConfig { Id = id, Enabled = true, ApiKeyProtected = "test-encrypted", ClaudeConfigDir = "untouched-profile", AvailableModels = ["sonnet"], ModelsSyncedAt = DateTimeOffset.Now };
        var row = new ProviderEdit(original);
        row.Disconnect(removeKey);
        Assert.False(row.IsConnected);
        Assert.False(row.Enabled);
        Assert.Empty(row.AvailableModels);
        Assert.Null(row.ModelsSyncedAt);
        Assert.Equal(keepsKey ? "Key saved" : "No key saved", row.KeyState);
        Assert.Equal("untouched-profile", row.ClaudeConfigDir);
        Assert.Single(original.AvailableModels);
    }
}
