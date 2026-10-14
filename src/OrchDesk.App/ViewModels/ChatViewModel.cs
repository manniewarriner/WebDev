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

public sealed record ProviderOption(string? Id, string Label);
public enum ChatMode { Chat, Supervisor, CostRouter }

public sealed partial class ChatViewModel : ObservableObject
{
    private readonly AppServices _services;
    private Conversation? _conversation;
    private CancellationTokenSource? _cts;

    public ObservableCollection<ChatItem> Items { get; } = [];
    public ObservableCollection<ProviderOption> ProviderOptions { get; } = [];
    public IReadOnlyList<ChatMode> Modes { get; } = [ChatMode.Chat, ChatMode.Supervisor, ChatMode.CostRouter];

    [ObservableProperty, NotifyCanExecuteChangedFor(nameof(SendCommand))] private string _input = "";
    [ObservableProperty, NotifyCanExecuteChangedFor(nameof(SendCommand)), NotifyPropertyChangedFor(nameof(WorkspaceLabel))] private string? _workspace;
    [ObservableProperty, NotifyCanExecuteChangedFor(nameof(SendCommand), nameof(StopCommand))] private bool _isBusy;
    [ObservableProperty] private ProviderOption? _selectedProvider;
    [ObservableProperty] private ChatMode _mode = ChatMode.Chat;
    [ObservableProperty] private string _activeModel = "";

    public string WorkspaceLabel => Workspace is null ? "Choose a project folder" : Workspace;

    /// <summary>Raised whenever content is added so the view can keep the list scrolled to the bottom.</summary>
    public event Action? ContentAdded;

    public ChatViewModel(AppServices services)
    {
        _services = services;
        _services.Rebuilt += ReloadProviders;
        ReloadProviders();
        if (services.Config.LastWorkspace is { } ws && Directory.Exists(ws)) Workspace = ws;
    }

    private void ReloadProviders()
    {
        var keep = SelectedProvider?.Id ?? _services.Config.LastProviderId;
        ProviderOptions.Clear();
        ProviderOptions.Add(new ProviderOption(null, "Auto — PC first, cloud when it's off"));
        foreach (var p in _services.Providers) ProviderOptions.Add(new ProviderOption(p.Id, p.DisplayName));
        SelectedProvider = ProviderOptions.FirstOrDefault(o => o.Id == keep) ?? ProviderOptions[0];
        _conversation = null; // pick up the new rotation chain on the next message; history is kept in Items only
    }

    partial void OnSelectedProviderChanged(ProviderOption? value)
    {
        _services.Config.LastProviderId = value?.Id;
        if (_conversation is not null) _conversation.PreferredProviderId = value?.Id;
    }

    partial void OnWorkspaceChanged(string? value)
    {
        _conversation = null;
        _services.Config.LastWorkspace = value;
        _services.Store.Save(_services.Config);
    }

    [RelayCommand]
    private void PickFolder()
    {
        var dlg = new Microsoft.Win32.OpenFolderDialog { Title = "Choose the project folder OrchDesk may work in" };
        if (Workspace is not null) dlg.InitialDirectory = Workspace;
        if (dlg.ShowDialog() == true)
        {
            Workspace = dlg.FolderName;
            Items.Add(new StatusItem($"Working in {Workspace}"));
        }
    }

    [RelayCommand(CanExecute = nameof(CanSend))]
    private async Task SendAsync()
    {
        var text = Input.Trim();
        Input = "";
        Items.Add(new UserItem(text));
        ContentAdded?.Invoke();
        IsBusy = true;
        _cts = new CancellationTokenSource();

        try
        {
            await foreach (var ev in StartRun(text, _cts.Token)) Render(ev);
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
            foreach (var t in Items.OfType<ToolItem>().Where(t => t.IsRunning)) t.IsRunning = false;
            IsBusy = false;
            _current = null;
            _cts.Dispose();
            _cts = null;
            ContentAdded?.Invoke();
        }
    }

    private bool CanSend() => !IsBusy && Workspace is not null && !string.IsNullOrWhiteSpace(Input);

    [RelayCommand(CanExecute = nameof(IsBusy))]
    private void Stop() => _cts?.Cancel();

    [RelayCommand]
    private void NewChat()
    {
        _cts?.Cancel();
        _conversation = null;
        Items.Clear();
    }

    private IAsyncEnumerable<ChatEvent> StartRun(string text, CancellationToken ct)
    {
        var gate = new PermissionGate(_services.CreatePolicy(), ApproveAsync);
        var loop = _services.CreateLoop(gate);
        var rotator = _services.Rotator;
        var sup = _services.Config.Supervisor;

        switch (Mode)
        {
            case ChatMode.Supervisor:
                var planner = rotator.Find(sup.PlannerProviderId);
                var worker = rotator.Find(sup.WorkerProviderId) is { } w && rotator.IsUsable(w) ? w.Provider : rotator.Current();
                if (planner is null || worker is null)
                    return One(new ErrorEvent($"Supervisor needs planner '{sup.PlannerProviderId}' and a worker model. Set them in Settings."));
                return new Supervisor(loop) { MaxRounds = sup.MaxRounds }.RunAsync(text, planner.Provider, worker, sup.TestCommand, Workspace!, ct);

            case ChatMode.CostRouter:
                var tiers = sup.CostTiers.Select(id => rotator.Find(id)).Where(a => a is not null && rotator.IsUsable(a)).Select(a => a!.Provider).ToList();
                if (tiers.Count == 0) return One(new ErrorEvent("No cost-router tiers are available. Check Settings → Supervisor."));
                return new CostRouter(loop).RunAsync(text, tiers, sup.TestCommand, Workspace!, ct);

            default:
                // Each run gets a fresh loop/gate (settings may have changed) but carries the history forward.
                var convo = new Conversation(loop, rotator, Workspace!) { PreferredProviderId = SelectedProvider?.Id };
                if (_conversation is not null) convo.Messages.AddRange(_conversation.Messages);
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
                ActiveModel = s.Text[6..];
                _current = null;
                break;
            case StatusUpdate s:
                Items.Add(new StatusItem(s.Text));
                _current = null;
                break;
            case TextDelta d:
                if (_current is null) { _current = new AssistantItem(ActiveModel); Items.Add(_current); }
                _current.Append(d.Text);
                break;
            case ToolCallRequested r:
                _current = null;
                Items.Add(new ToolItem(r.Call.Id, ToolTitles.Describe(r.Call.Name, r.Call.ArgumentsJson), r.Call.ArgumentsJson));
                break;
            case ToolCallResult r:
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
