using OrchDesk.App.Services;
using OrchDesk.App.ViewModels;
using OrchDesk.Core.Config;
using OrchDesk.Core.Models;
using OrchDesk.Core.Providers;

namespace OrchDesk.Tests;

public sealed class ComposerSendTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "OrchDesk-send-" + Guid.NewGuid());
    private readonly ConfigStore _store;
    public ComposerSendTests() => _store = new ConfigStore(_root);
    private ChatViewModel Create(params IModelProvider[] providers) =>
        new(new AppServices(store: _store, chatOnly: true, providers: providers), Path.Combine(_root, "Scratch"));

    [Fact]
    public async Task SendsWithoutFolderAndCreatesScratchOnlyOnSend()
    {
        var provider = new FakeProvider();
        var vm = Create(provider);
        vm.Input = "Hello";
        Assert.Null(vm.Workspace);
        Assert.False(Directory.Exists(vm.EffectiveWorkspace));
        Assert.Null(vm.SendBlockedReason);
        Assert.True(vm.SendCommand.CanExecute(null));
        await vm.SendCommand.ExecuteAsync(null);
        Assert.True(Directory.Exists(vm.EffectiveWorkspace));
        Assert.Equal(vm.EffectiveWorkspace, provider.Workspace);
        Assert.Null(vm.Workspace);
        Assert.Null(_store.Load().LastWorkspace);
        Assert.Contains(vm.Items.OfType<AssistantItem>(), a => a.Text == "Hello back");
    }

    [Fact]
    public void ReasonsAndCommandNotificationsFollowInputAndBusy()
    {
        var vm = Create(new FakeProvider());
        Assert.Equal("Type a message", vm.SendBlockedReason);
        Assert.False(vm.SendCommand.CanExecute(null));
        var changed = 0;
        vm.SendCommand.CanExecuteChanged += (_, _) => changed++;
        vm.Input = "Hello";
        Assert.True(changed > 0);
        Assert.True(vm.SendCommand.CanExecute(null));
        vm.IsBusy = true;
        Assert.Equal("Working on the previous message", vm.SendBlockedReason);
        Assert.False(vm.SendCommand.CanExecute(null));
        vm.TrySend();
        Assert.Equal(vm.SendBlockedReason, vm.ComposerNotice);
        vm.IsBusy = false;
        vm.Input = " ";
        Assert.Equal("Type a message", vm.SendBlockedReason);
        vm.TrySend();
        Assert.Null(vm.ComposerNotice);
    }

    [Fact]
    public void MissingOrUnavailableSelectedProviderBlocks()
    {
        var vm = Create();
        vm.Input = "Hello";
        Assert.Equal("No model available - connect one in Integrations", vm.SendBlockedReason);
        Assert.False(vm.SendCommand.CanExecute(null));
        vm = Create(new FakeProvider());
        vm.Input = "Hello";
        vm.SelectedProvider = new ModelPickerOption("missing", "Missing");
        Assert.Equal("No model available - connect one in Integrations", vm.SendBlockedReason);
        Assert.False(vm.SendCommand.CanExecute(null));
    }

    [Theory]
    [InlineData("--send")]
    [InlineData("--send", " ")]
    [InlineData("--send", "Hi", "--provider")]
    [InlineData("--send", "Hi", "--config")]
    [InlineData("--send", "Hi", "--send", "Again")]
    public void InvalidSendArgumentsThrow(params string[] args) => Assert.Throws<ArgumentException>(() => SendOptions.Parse(args));

    [Fact]
    public void ParsesSendArguments()
    {
        Assert.Null(SendOptions.Parse(["--page", "composer-ready"]));
        Assert.Equal(new SendOptions("Hello world", "fake", "test.json"), SendOptions.Parse(["--send", "Hello world", "--provider", "fake", "--config", "test.json"]));
    }

    public void Dispose() { _store.Dispose(); if (Directory.Exists(_root)) Directory.Delete(_root, true); }

    private sealed class FakeProvider : IModelProvider
    {
        public string Id => "fake";
        public string DisplayName => "Fake";
        public bool IsAgentic => false;
        public bool IsLocal => true;
        public string? Workspace { get; private set; }
        public Task<ProviderHealth> CheckHealthAsync(CancellationToken ct) => Task.FromResult(ProviderHealth.Online);
        public async IAsyncEnumerable<ChatEvent> StreamChatAsync(ChatRequest request, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct)
        {
            Workspace = request.WorkingDirectory;
            Assert.Empty(request.Tools);
            await Task.Yield();
            yield return new TextDelta("Hello back");
            yield return new Done();
        }
    }
}

