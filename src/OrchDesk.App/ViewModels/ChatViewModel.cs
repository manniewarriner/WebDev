using System.Collections.ObjectModel;
using System.IO;
using System.Text.Json.Nodes;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OrchDesk.App.Services;
using OrchDesk.Core.Models;
using OrchDesk.Core.Orchestration;
using OrchDesk.Core.Permissions;

namespace OrchDesk.App.ViewModels;


public enum ChatViewState { Home, Thread }

public enum ChatMode { Chat, CostRouter }

public sealed partial class ChatViewModel : ObservableObject
{
    private readonly AppServices _services;
    private readonly IFolderPicker _folderPicker;
    public ChatViewState ViewState => Items.Count > 0 || IsBusy ? ChatViewState.Thread : ChatViewState.Home;
    partial void OnIsBusyChanged(bool value) { OnPropertyChanged(nameof(ViewState)); PickFolderCommand.NotifyCanExecuteChanged(); }
    private Conversation? _conversation;
    private CancellationTokenSource? _cts;
    private readonly string _scratchWorkspace;
    private OrchDesk.Core.Accounts.AccountRotator? _observedRotator;
    public string EffectiveWorkspace => Workspace ?? _scratchWorkspace;
    public bool UsesScratchWorkspace => Workspace is null;
    public string? SendBlockedReason => IsBusy ? "Working on the previous message"
        : string.IsNullOrWhiteSpace(Input) ? "Type a message"
        : null;
    [ObservableProperty] private string? _composerNotice;

    private bool HasAvailableProvider() => SelectedProvider?.Id is { } id
        ? _services.Rotator.Find(id) is { } account && _services.Rotator.IsUsable(account)
        : _services.Rotator.Current() is not null;

    private void RefreshSendState()
    {
        OnPropertyChanged(nameof(SendBlockedReason));
        SendCommand.NotifyCanExecuteChanged();
    }

    public void TrySend()
    {
        if (SendBlockedReason is null) SendCommand.Execute(null);
        else if (!string.IsNullOrWhiteSpace(Input)) ComposerNotice = SendBlockedReason;
    }
    partial void OnInputChanged(string value) { ComposerNotice = null; }


    public ObservableCollection<string> RecentFolders { get; } = [];
    [RelayCommand]
    private void OpenRecentFolder(string? path)
    {
        if (IsBusy || path is null) return;
        if (Directory.Exists(path)) Workspace = path;
        else { RecentFolders.Remove(path); ComposerNotice = "That folder is no longer available. Choose another folder."; }
    }

    public ObservableCollection<ChatItem> Items { get; } = [];
    public ObservableCollection<OrchDesk.Core.Config.ModelPickerOption> ProviderOptions { get; } = [];
    public IReadOnlyList<ChatMode> Modes { get; } = [ChatMode.Chat, ChatMode.CostRouter];

    [ObservableProperty, NotifyPropertyChangedFor(nameof(SendBlockedReason)), NotifyCanExecuteChangedFor(nameof(SendCommand))] private string _input = "";
    [ObservableProperty, NotifyCanExecuteChangedFor(nameof(SendCommand)), NotifyPropertyChangedFor(nameof(WorkspaceLabel), nameof(UsesScratchWorkspace), nameof(EffectiveWorkspace))] private string? _workspace;
    [ObservableProperty, NotifyPropertyChangedFor(nameof(SendBlockedReason)), NotifyCanExecuteChangedFor(nameof(SendCommand), nameof(StopCommand))] private bool _isBusy;
    [ObservableProperty] private OrchDesk.Core.Config.ModelPickerOption? _selectedProvider;
    [ObservableProperty] private ChatMode _mode = ChatMode.Chat;
    [ObservableProperty] private bool _isThinking;
    [ObservableProperty] private string _activeModel = "";
    [ObservableProperty] private bool _shutdownWhenDone;

    [ObservableProperty] private string _editorText = "";
    [ObservableProperty] private string _editorPath = "";
    [RelayCommand]
    private void OpenFile()
    {
        if (Workspace is null) return;
        var dialog = new Microsoft.Win32.OpenFileDialog { InitialDirectory = Workspace };
        if (dialog.ShowDialog() != true) return;
        try {
            var path = OrchDesk.Core.Tools.WorkspacePath.Resolve(Workspace, dialog.FileName);
            var text = File.ReadAllText(path);
            EditorPath = path; EditorText = text;
        } catch (Exception ex) { Items.Add(new ErrorItem(ex.Message)); }
    }
    [RelayCommand]
    private void SaveFile()
    {
        if (Workspace is null || string.IsNullOrEmpty(EditorPath)) return;
        try { File.WriteAllText(OrchDesk.Core.Tools.WorkspacePath.Resolve(Workspace, EditorPath), EditorText); }
        catch (Exception ex) { Items.Add(new ErrorItem(ex.Message)); }
    }

    public string WorkspaceLabel => Workspace is null ? "Choose a project folder" : Workspace;

    /// <summary>Raised whenever content is added so the view can keep the list scrolled to the bottom.</summary>
    public event Action? ContentAdded;

    public ChatViewModel(AppServices services, string? scratchWorkspace = null, IFolderPicker? folderPicker = null)
    {
        _services = services;
        _folderPicker = folderPicker ?? new FolderPicker();
        Items.CollectionChanged += (_, _) => OnPropertyChanged(nameof(ViewState));
        _scratchWorkspace = scratchWorkspace ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Documents", "OrchDesk", "Scratch");
        _shutdownWhenDone = services.Config.Overnight.ShutdownWhenDone;
        _services.Rebuilt += ReloadProviders;
        ReloadProviders();
        foreach (var path in services.Config.RecentWorkspaces.Where(Directory.Exists).Take(5)) RecentFolders.Add(path);
        if (services.Config.LastWorkspace is { } ws && Directory.Exists(ws)) Workspace = ws;
    }

    private void ReloadProviders()
    {
        ShutdownWhenDone = _services.Config.Overnight.ShutdownWhenDone;
        var keepModel = SelectedProvider?.Model;
        var keep = SelectedProvider?.Id ?? _services.Config.LastProviderId;
        ProviderOptions.Clear();
        ProviderOptions.Add(new OrchDesk.Core.Config.ModelPickerOption(null, "Auto — PC first, cloud when it's off"));
        foreach (var p in OrchDesk.Core.Config.ModelPickerOption.Expand(_services.Config.Providers)) ProviderOptions.Add(p);
        SelectedProvider = ProviderOptions.FirstOrDefault(o => o.Id == keep && (keepModel is null || o.Model == keepModel)) ?? ProviderOptions[0];
        if (_observedRotator is not null) _observedRotator.Changed -= RefreshSendState;
        _observedRotator = _services.Rotator;
        _observedRotator.Changed += RefreshSendState;
        RefreshSendState();
        _conversation = null; // pick up the new rotation chain on the next message; history is kept in Items only
    }

    partial void OnSelectedProviderChanged(OrchDesk.Core.Config.ModelPickerOption? value)
    {
        RefreshSendState();
        _services.Config.LastProviderId = value?.Id;
        if (_conversation is not null) _conversation.PreferredProviderId = value?.Id;
    }

    partial void OnWorkspaceChanged(string? value)
    {
        _conversation = null;
        Items.Clear();
        EditorPath = "";
        EditorText = "";
        _services.Config.LastWorkspace = value;
        if (value is not null) {
            RecentFolders.Remove(value); RecentFolders.Insert(0, value);
            while (RecentFolders.Count > 5) RecentFolders.RemoveAt(5);
            _services.Config.RecentWorkspaces = RecentFolders.ToList();
        }
        if (!_services.IsScreenshot) _services.Store.Save(_services.Config);
    }

    private bool CanPickFolder() => !IsBusy;
    [RelayCommand(CanExecute = nameof(CanPickFolder))]
    private void PickFolder()
    {
        if (!CanPickFolder()) return;
        var path = _folderPicker.PickFolder(Workspace);
        if (path is not null && Directory.Exists(path) && !IsBusy) Workspace = Path.GetFullPath(path);
    }

    [RelayCommand(CanExecute = nameof(CanSend))]
    private async Task SendAsync()
    {
        if (!CanSend()) return;
        var text = Input.Trim();

        bool isOvernight = false;
        string goal = "";

        if (!_services.ChatOnly && (text.Equals("/overnight", StringComparison.OrdinalIgnoreCase) ||
            text.StartsWith("/overnight ", StringComparison.OrdinalIgnoreCase)))
        {
            isOvernight = true;
            goal = text.StartsWith("/overnight ", StringComparison.OrdinalIgnoreCase)
                ? text[11..].Trim()
                : "";
        }

        if (isOvernight && string.IsNullOrEmpty(goal))
        {
            return;
        }

        Input = "";
        Items.Add(new UserItem(text));
        ContentAdded?.Invoke();
        IsBusy = true;
        IsThinking = true;
        _cts = new CancellationTokenSource();

        try
        {
            Directory.CreateDirectory(EffectiveWorkspace);
            if (isOvernight)
                await RunOvernightAsync(goal, _cts.Token);
            else await foreach (var ev in StartRun(text, _cts.Token)) Render(ev);
        }
        catch (OperationCanceledException)
        {
            Items.Add(new StatusItem("Stopped."));
        }
        catch (Exception ex)
        {
            Items.Add(new ErrorItem(ex.Message));
        }
        finally
        {
            foreach (var a in Items.OfType<ApprovalItem>().Where(a => a.IsPending)) a.Cancel();
            foreach (var t in Items.OfType<ToolItem>().Where(t => t.IsRunning)) { t.IsError = true; t.Output ??= "Interrupted before a result was returned."; t.IsRunning = false; }
            foreach (var answer in Items.OfType<AssistantItem>()) answer.IsStreaming = false;
            IsThinking = false;
            IsBusy = false;
            _current = null;
            _cts.Dispose();
            _cts = null;
            ContentAdded?.Invoke();
        }
    }

    [RelayCommand]
    private void OvernightSkill()
    {
        var lowerInput = Input.TrimStart();
        if (lowerInput.Equals("/overnight", StringComparison.OrdinalIgnoreCase) ||
            lowerInput.StartsWith("/overnight ", StringComparison.OrdinalIgnoreCase))
        {
            // Already prefixed, leave unchanged to preserve goal text and whitespace.
            return;
        }

        Input = "/overnight " + Input;
    }

    private string RunsDirectory => Path.Combine(_services.Store.Directory, "runs");
    private async Task RunOvernightAsync(string goal, CancellationToken ct, OrchDesk.Core.Overnight.OvernightRun? existing = null)
    {
        var id = existing?.ManagerId ?? SelectedProvider?.Id;
        var manager = id is null ? _services.Providers.FirstOrDefault(p => !p.IsAgentic) : _services.CreateRunRotator(SelectedProvider).Find(id)?.Provider;
        if (manager is null) throw new InvalidOperationException("Configure an API manager model for overnight runs.");
        ActiveModel = manager.DisplayName;
        var run = existing ?? new OrchDesk.Core.Overnight.OvernightRun {
            Goal = goal, Workspace = EffectiveWorkspace, ManagerId = manager.Id,
            TestCommand = _services.Config.Supervisor.TestCommand, MaxSteps = _services.Config.MaxSteps,
            WorkerIds = [_services.Config.Supervisor.WorkerProviderId], MaxRounds = _services.Config.Supervisor.MaxRounds, ShutdownWhenDone = ShutdownWhenDone,
            Deadline = DateTimeOffset.UtcNow.AddMinutes(_services.Config.Overnight.MaxMinutesPerTask)
        };
        var skill = new OrchDesk.Core.Overnight.OvernightRoundRunner(RunsDirectory, _services.Tools, _services.CreatePolicy());
        skill.Progress += Render;
        Items.Add(new StatusItem("Overnight run ? manager " + manager.DisplayName));
        var job = new ToolItem(run.Id, "Overnight · " + goal, System.Text.Json.JsonSerializer.Serialize(new { goal, run.MaxRounds, run.TestCommand }));
        Items.Add(job);
        try
        {
            var summary = await skill.RunAsync(run, _services.Providers, ct);
            job.Output = summary;
            job.IsError = run.Status is "Needs attention" or "Deadline reached";
            var item = new AssistantItem(manager.DisplayName); item.Append(summary); Items.Add(item);
        }
        catch { job.IsError = true; job.Output = "Run interrupted; checkpoint retained."; throw; }
        finally { job.IsRunning = false; }
    }

    public async Task ResumeOvernightAsync()
    {
        if (!Directory.Exists(RunsDirectory)) return;
        foreach (var path in Directory.GetFiles(RunsDirectory, "checkpoint.json", SearchOption.AllDirectories))
        {
            try {
                var run = System.Text.Json.JsonSerializer.Deserialize<OrchDesk.Core.Overnight.OvernightRun>(File.ReadAllText(path));
                if (run is null) continue;
                if (run.Status != "Running") {
                    var report = Path.Combine(Path.GetDirectoryName(path)!, "morning-summary.md");
                    if (File.Exists(report)) Items.Add(new StatusItem(File.ReadAllText(report)));
                    continue;
                }
                IsBusy = true;
                _cts = new CancellationTokenSource();
                await RunOvernightAsync(run.Goal, _cts.Token, run);
            }
            catch (Exception) { Items.Add(new ErrorItem("Overnight checkpoint retained; manager could not resume.")); }
            finally { IsThinking = false;
            IsBusy = false; _cts?.Dispose(); _cts = null; }
        }
    }

    private bool CanSend() => SendBlockedReason is null;

    [RelayCommand(CanExecute = nameof(IsBusy))]
    private void Stop() => _cts?.Cancel();

    [RelayCommand]
    private async Task NewChatAsync()
    {
        _cts?.Cancel();
        if (SendCommand.ExecutionTask is { } pending) await pending;
        _conversation = null;
        Items.Clear();
    }

    public Conversation? CurrentConversation => _conversation;

    public void Restore(ChatItem[] items, Conversation? conversation)
    {
        if (IsBusy) return;
        _conversation = conversation;
        if (_conversation is null) {
            var gate = new PermissionGate(_services.CreatePolicy(), ApproveAsync);
            _conversation = new Conversation(_services.CreateLoop(gate), _services.CreateRunRotator(SelectedProvider), EffectiveWorkspace);
            foreach (var item in items) { if(item is UserItem u) _conversation.Messages.Add(ChatMessage.User(u.Text)); else if(item is AssistantItem a) _conversation.Messages.Add(ChatMessage.Assistant(a.Text)); }
        }
        Items.Clear();
        foreach (var item in items) Items.Add(item);
        ContentAdded?.Invoke();
    }

    private IAsyncEnumerable<ChatEvent> StartRun(string text, CancellationToken ct)
    {
        var gate = new PermissionGate(_services.CreatePolicy(), ApproveAsync);
        var loop = _services.CreateLoop(gate);
        var rotator = _services.CreateRunRotator(SelectedProvider);
        var sup = _services.Config.Supervisor;

        switch (Mode)
        {
            case ChatMode.CostRouter:
                var tiers = sup.CostTiers.Select(id => rotator.Find(id)).Where(a => a is not null && rotator.IsUsable(a)).Select(a => a!.Provider).ToList();
                if (tiers.Count == 0) return One(new ErrorEvent("No cost-router tiers are available. Check Settings → Supervisor."));
                return new CostRouter(loop).RunAsync(text, tiers, sup.TestCommand, EffectiveWorkspace, ct);

            default:
                // Each run gets a fresh loop/gate (settings may have changed) but carries the history forward.
                var convo = new Conversation(loop, rotator, EffectiveWorkspace) { PreferredProviderId = SelectedProvider?.Id };
                if (_conversation is not null) { convo.Messages.AddRange(_conversation.Messages); convo.StickyProviderId = _conversation.StickyProviderId; }
                else if (_services.ChatOnly) convo.Messages.Add(ChatMessage.System("You are OrchDesk, a helpful chat assistant. Answer the user directly. No tools are available."));
                _conversation = convo;
                return convo.SendAsync(text, ct);
        }
    }

    private static async IAsyncEnumerable<ChatEvent> One(ChatEvent ev)
    {
        await Task.CompletedTask;
        yield return ev;
    }

    private async Task<ApprovalAnswer> ApproveAsync(ApprovalRequest request, CancellationToken ct)
    {
        var item = await Application.Current.Dispatcher.InvokeAsync(() =>
        {
            var a = new ApprovalItem(request);
            Items.Add(a);
            ContentAdded?.Invoke();
            return a;
        });
        using var reg = ct.Register(item.Cancel);
        return await item.Answer;
    }

    private AssistantItem? _current;

    private void Render(ChatEvent ev)
    {
        switch (ev)
        {
            case StatusUpdate s when s.Text.StartsWith("Using "):
                ActiveModel = _conversation?.LastProvider is { } answering ? OrchDesk.Core.Config.ModelDisplayNames.Get(answering.ModelId) + (_conversation.Attempts.Count > 0 ? " ? fell back from " + (_conversation.Attempts.Any(a => a.ProviderId is "remote-pc" or "local-pc") ? "Desktop PC" : _conversation.Attempts[0].ProviderId) : "") : s.Text[6..];
                if (_current is not null) _current.IsStreaming = false;
                _current = null;
                break;
            case StatusUpdate s:
                if (s.Text == "Thinking...") { IsThinking = true; break; }
                Items.Add(new StatusItem(s.Text));
                if (_current is not null) _current.IsStreaming = false;
                _current = null;
                break;
            case ReasoningDelta r:
                if (_current is null) { _current = new AssistantItem(ActiveModel) { ProviderId = _conversation?.LastProvider?.Id }; Items.Add(_current); }
                _current.Reasoning += r.Text;
                IsThinking = true;
                break;
            case TextDelta d:
                IsThinking = false;
                if (_current is null) { _current = new AssistantItem(ActiveModel) { ProviderId = _conversation?.LastProvider?.Id, IsStreaming = true }; Items.Add(_current); }
                _current.IsStreaming = true;
                _current.Append(d.Text);
                break;
            case ToolCallRequested r:
                IsThinking = false;
                if (_current is not null) _current.IsStreaming = false;
                _current = null;
                Items.Add(new ToolItem(r.Call.Id, ToolTitles.Describe(r.Call.Name, r.Call.ArgumentsJson), r.Call.ArgumentsJson));
                break;
            case ToolCallResult r:
                IsThinking = true;
                var tool = Items.OfType<ToolItem>().LastOrDefault(t => t.CallId == r.CallId);
                if (tool is null) { tool = new ToolItem(r.CallId, r.Name == "tests" ? "Ran the tests" : r.Name, ""); Items.Add(tool); }
                tool.Output = r.Output;
                tool.IsError = r.IsError;
                tool.IsRunning = false;
                if (r.IsError && r.Name == "tests") tool.IsExpanded = true;
                break;
            case ErrorEvent e:
                Items.Add(new ErrorItem(e.Message));
                break;
            case UsageLimitHit u:
                Items.Add(new ErrorItem($"Usage limit: {u.Message}"));
                break;
            case ProviderUnavailable p:
                Items.Add(new ErrorItem(p.Message));
                break;
            case Done when _current is not null:
                _current.IsStreaming = false;
                break;
            case Done d when _current is null && !string.IsNullOrWhiteSpace(d.FinalText) && Items.LastOrDefault() is not AssistantItem:
                Items.Add(new StatusItem(d.FinalText!));
                break;
        }
        ContentAdded?.Invoke();
    }
}

/// <summary>Human-readable one-liners for tool calls, for both OrchDesk's tools and Claude Code's own.</summary>
public static class ToolTitles
{
    public static string Describe(string name, string argsJson)
    {
        JsonObject? a = null;
        try { a = JsonNode.Parse(argsJson) as JsonObject; } catch (System.Text.Json.JsonException) { }
        string Arg(params string[] keys) => keys.Select(k => a?[k]?.ToString()).FirstOrDefault(v => !string.IsNullOrEmpty(v)) ?? "";

        return name switch
        {
            "read_file" or "Read" => $"Reading {Arg("path", "file_path")}",
            "write_file" or "Write" => $"Writing {Arg("path", "file_path")}",
            "edit_file" or "Edit" or "MultiEdit" => $"Editing {Arg("path", "file_path")}",
            "delete_file" => $"Deleting {Arg("path")}",
            "list_files" or "LS" or "Glob" => $"Listing {(Arg("path", "pattern") is { Length: > 0 } p ? p : "files")}",
            "search" or "Grep" => $"Searching for “{Arg("pattern")}”",
            "run_command" or "Bash" => $"Running {Arg("command")}",
            "git_status" => "Checking git status",
            "git_diff" => "Reading the diff",
            "git_commit" => $"Committing “{Arg("message")}”",
            "git_push" => "Pushing the branch",
            "web_fetch" or "WebFetch" => $"Fetching {Arg("url")}",
            "TodoWrite" => "Updating its task list",
            _ => name,
        };
    }
}
