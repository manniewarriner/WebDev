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

public sealed record ProviderOption(string? Id, string Label) { public override string ToString() => Label; }
public enum ChatMode { Chat, CostRouter }

public sealed partial class ChatViewModel : ObservableObject
{
    private readonly AppServices _services;
    private Conversation? _conversation;
    private CancellationTokenSource? _cts;

    public ObservableCollection<ChatItem> Items { get; } = [];
    public ObservableCollection<ProviderOption> ProviderOptions { get; } = [];
    public IReadOnlyList<ChatMode> Modes { get; } = [ChatMode.Chat, ChatMode.CostRouter];

    [ObservableProperty, NotifyCanExecuteChangedFor(nameof(SendCommand))] private string _input = "";
    [ObservableProperty, NotifyCanExecuteChangedFor(nameof(SendCommand)), NotifyPropertyChangedFor(nameof(WorkspaceLabel))] private string? _workspace;
    [ObservableProperty, NotifyCanExecuteChangedFor(nameof(SendCommand), nameof(StopCommand))] private bool _isBusy;
    [ObservableProperty] private ProviderOption? _selectedProvider;
    [ObservableProperty] private ChatMode _mode = ChatMode.Chat;
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

    public ChatViewModel(AppServices services)
    {
        _services = services;
        _shutdownWhenDone = services.Config.Overnight.ShutdownWhenDone;
        _services.Rebuilt += ReloadProviders;
        ReloadProviders();
        if (services.Config.LastWorkspace is { } ws && Directory.Exists(ws)) Workspace = ws;
    }

    private void ReloadProviders()
    {
        ShutdownWhenDone = _services.Config.Overnight.ShutdownWhenDone;
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
            if (text.StartsWith("/overnight ", StringComparison.OrdinalIgnoreCase))
                await RunOvernightAsync(text[11..].Trim(), _cts.Token);
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
            foreach (var t in Items.OfType<ToolItem>().Where(t => t.IsRunning)) t.IsRunning = false;
            IsBusy = false;
            _current = null;
            _cts.Dispose();
            _cts = null;
            ContentAdded?.Invoke();
        }
    }

    [RelayCommand]
    private void OvernightSkill() => Input = "/overnight " + Input;

    private string RunsDirectory => Path.Combine(_services.Store.Directory, "runs");
    private async Task RunOvernightAsync(string goal, CancellationToken ct, OrchDesk.Core.Overnight.OvernightRun? existing = null)
    {
        var id = existing?.ManagerId ?? SelectedProvider?.Id;
        var manager = id is null ? _services.Providers.FirstOrDefault(p => !p.IsAgentic) : _services.Providers.FirstOrDefault(p => p.Id == id);
        if (manager is null) throw new InvalidOperationException("Configure an API manager model for overnight runs.");
        ActiveModel = manager.DisplayName;
        var run = existing ?? new OrchDesk.Core.Overnight.OvernightRun {
            Goal = goal, Workspace = Workspace!, ManagerId = manager.Id,
            TestCommand = _services.Config.Supervisor.TestCommand, MaxSteps = _services.Config.MaxSteps,
            WorkerIds = [_services.Config.Supervisor.WorkerProviderId], MaxRounds = _services.Config.Supervisor.MaxRounds, ShutdownWhenDone = ShutdownWhenDone,
            Deadline = DateTimeOffset.UtcNow.AddMinutes(_services.Config.Overnight.MaxMinutesPerTask)
        };
        var skill = new OrchDesk.Core.Overnight.OvernightRoundRunner(RunsDirectory, _services.Tools, _services.CreatePolicy());
        skill.Progress += Render;
        Items.Add(new StatusItem("Overnight run ? manager " + manager.DisplayName));
        var summary = await skill.RunAsync(run, _services.Providers, ct);
        var item = new AssistantItem(manager.DisplayName); item.Append(summary); Items.Add(item);
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
            finally { IsBusy = false; _cts?.Dispose(); _cts = null; }
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
