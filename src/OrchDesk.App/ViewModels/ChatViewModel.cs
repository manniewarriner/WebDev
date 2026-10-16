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
    public AgentsPanelViewModel Agents { get; } = new();
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
    [ObservableProperty] private int _workModeIndex = 1;
    [ObservableProperty, NotifyPropertyChangedFor(nameof(FullAccess))] private int _permissionIndex;
    [ObservableProperty] private bool _changesVisible;
    [ObservableProperty] private string _actionToast = "";
    [ObservableProperty] private string _thinkingLabel = "Thinking · 0s";
    public bool FullAccess => PermissionIndex == 1;
    public ObservableCollection<ChangeItem> Changes { get; } = [];
    private ChangeLog _changeLog = new();
    private readonly System.Diagnostics.Stopwatch _thinkingWatch = new();
    private readonly System.Windows.Threading.DispatcherTimer _thinkingTimer = new() { Interval = TimeSpan.FromSeconds(1) };
    private ResponseUsage? _runUsage;
    private string _runInput = "";
    private int _runFirstItem;
    partial void OnIsThinkingChanged(bool value) { if (value) _thinkingWatch.Start(); else _thinkingWatch.Stop(); }
    partial void OnPermissionIndexChanged(int value) { _services.Config.SetPermission(EffectiveWorkspace, value == 1 ? OrchDesk.Core.Config.WorkspacePermission.FullAccess : OrchDesk.Core.Config.WorkspacePermission.Manual); if (!_services.IsScreenshot) _services.Store.Save(_services.Config); }
    partial void OnWorkModeIndexChanged(int value) { _services.Config.LastWorkMode = (OrchDesk.Core.Config.WorkMode)value; if (!_services.IsScreenshot) _services.Store.Save(_services.Config); }
    [RelayCommand] private void ToggleChanges() => ChangesVisible = !ChangesVisible;
    private void AttachChanges(OrchDesk.Core.Agent.AgentLoop loop)
    {
        loop.Changes = _changeLog;
    }
    private void ResetChanges()
    {
        Changes.Clear(); _changeLog = new();
        _changeLog.Added += entry => Application.Current.Dispatcher.Invoke(() => { Changes.Add(new(entry)); ActionToast = $"{entry.Action}: {(entry.Path.Length == 0 ? "completed" : System.IO.Path.GetFileName(entry.Path))}"; var timer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(4) }; timer.Tick += (_, _) => { ActionToast = ""; timer.Stop(); }; timer.Start(); });
    }

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
        _workModeIndex = (int)services.Config.LastWorkMode;
        ResetChanges();
        _thinkingTimer.Tick += (_, _) => ThinkingLabel = $"Thinking · {_thinkingWatch.Elapsed.TotalSeconds:0}s";
        _folderPicker = folderPicker ?? new FolderPicker();
        Items.CollectionChanged += (_, _) => OnPropertyChanged(nameof(ViewState));
        _scratchWorkspace = scratchWorkspace ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Documents", "OrchDesk", "Scratch");
        _permissionIndex = (int)services.Config.PermissionFor(_scratchWorkspace);
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
        ProviderOptions.Add(new OrchDesk.Core.Config.ModelPickerOption(null, "Auto model"));
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
        Agents.Update(null);
        ResetChanges();
        PermissionIndex = (int)_services.Config.PermissionFor(EffectiveWorkspace);
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

        if (WorkModeIndex == 1 && !_services.ChatOnly && (text.Equals("/overnight", StringComparison.OrdinalIgnoreCase) ||
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
        foreach (var plan in Items.OfType<PlanItem>().Where(p => p.IsPending)) plan.CancelCommand.Execute(null);
        Items.Add(new UserItem(text));
        _runFirstItem = Items.Count;
        ContentAdded?.Invoke();
        IsBusy = true;
        IsThinking = true;
        _runInput = text; _runUsage = null; _thinkingWatch.Restart(); _thinkingTimer.Start(); ThinkingLabel = "Thinking · 0s";
        _cts = new CancellationTokenSource();

        try
        {
            Directory.CreateDirectory(EffectiveWorkspace);
            if (isOvernight)
                await RunOvernightAsync(goal, _cts.Token);
            else {
                var planning = WorkModeIndex == 0;
                var firstItem = Items.Count;
                await foreach (var ev in StartRun(text, _cts.Token)) Render(ev);
                if (planning && !_cts.IsCancellationRequested) {
                    var planText = string.Join("\n", Items.Skip(firstItem).OfType<AssistantItem>().Select(a => a.Text));
                    var steps = OrchDesk.Core.Agent.WorkPlan.Parse(planText);
                    if (steps.Count > 0) Items.Add(new PlanItem(steps, RunApprovedPlan));
                }
            }
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
            _thinkingWatch.Stop(); _thinkingTimer.Stop();
            var finalAnswer = Items.Skip(_runFirstItem).OfType<AssistantItem>().LastOrDefault();
            if (finalAnswer is not null) {
                var usage = _runUsage ?? ResponseUsage.Estimate(_runInput, finalAnswer.Text + finalAnswer.Reasoning);
                finalAnswer.Usage = usage; finalAnswer.ThinkingSeconds = _thinkingWatch.Elapsed.TotalSeconds;
                finalAnswer.ThoughtSummary = usage.Format(_thinkingWatch.Elapsed);
                finalAnswer.UsageTooltip = usage.Tooltip(_conversation?.LastProvider?.DisplayName ?? finalAnswer.ProviderId ?? "", _conversation?.LastProvider?.ModelId ?? finalAnswer.ModelName);
            }
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
        Agents.Update(null);
        ResetChanges();
    }

    public Conversation? CurrentConversation => _conversation;

    public void Restore(ChatItem[] items, Conversation? conversation)
    {
        if (IsBusy) return;
        _conversation = conversation;
        if (conversation is not null) {
            _changeLog = conversation.Changes;
            Changes.Clear(); foreach (var entry in _changeLog.Entries) Changes.Add(new(entry));
        }
        if (_conversation is null) {
            ResetChanges();
            var gate = new PermissionGate(_services.CreatePolicy(), ApproveAsync);
            var loop = _services.CreateLoop(gate); AttachChanges(loop);
            _conversation = new Conversation(loop, _services.CreateRunRotator(SelectedProvider), EffectiveWorkspace);
            foreach (var item in items) { if(item is UserItem u) _conversation.Messages.Add(ChatMessage.User(u.Text)); else if(item is AssistantItem a) _conversation.Messages.Add(ChatMessage.Assistant(a.Text)); }
        }
        Items.Clear();
        Agents.Update(null);
        foreach (var item in items) Items.Add(item);
        ContentAdded?.Invoke();
    }

    private IAsyncEnumerable<ChatEvent> StartRun(string text, CancellationToken ct)
    {
        var policy = _services.CreatePolicy();
        policy.WorkspaceRoot = EffectiveWorkspace;
        policy.WorkspacePermission = _services.Config.PermissionFor(EffectiveWorkspace);
        var gate = new PermissionGate(policy, ApproveAsync);
        var loop = _services.CreateLoop(gate);
        AttachChanges(loop);
        var rotator = _services.CreateRunRotator(SelectedProvider);
        var sup = _services.Config.Supervisor;

        if (SelectedProvider?.Id is null) return RunAuto(text, gate, ct);
        switch (WorkModeIndex == 0 ? ChatMode.Chat : Mode)
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
                if (WorkModeIndex == 0) text = OrchDesk.Core.Agent.WorkPlan.Instruction + "\n\nTask: " + text;
                return convo.SendAsync(text, ct, readOnly: WorkModeIndex == 0);
        }
    }

    private async IAsyncEnumerable<ChatEvent> RunAuto(string text, PermissionGate gate, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct)
    {
        await _services.EnsureModelVerificationAsync();
        LastTeamJob = null; Agents.Update(null);
        var config = _services.Config.WorkspaceTeams.GetValueOrDefault(OrchDesk.Core.Config.AppConfig.WorkspaceKey(EffectiveWorkspace)) ?? _services.Config.Team;
        var available = new List<OrchDesk.Core.Providers.IModelProvider>();
        var health = new Dictionary<string, OrchDesk.Core.Providers.ProviderHealth>();
        foreach (var provider in _services.AutoModels(EffectiveWorkspace)) {
            if (_services.Usage.Pick([provider]) is null) continue;
            if (provider.IsAgentic || (provider.Id == config.PmProvider && provider.ModelId == config.PmModel)) {
                if (!health.TryGetValue(provider.Id, out var checkedHealth)) health[provider.Id] = checkedHealth = await provider.CheckHealthAsync(ct);
                if (checkedHealth != OrchDesk.Core.Providers.ProviderHealth.Online) continue;
            }
            available.Add(provider);
        }
        var models = available;
        var workers = models.Where(TeamRoles.IsWorker).ToList();
        var pm = models.FirstOrDefault(p => p.Id.Equals(config.PmProvider,StringComparison.OrdinalIgnoreCase) && p.ModelId.Equals(config.PmModel,StringComparison.OrdinalIgnoreCase));
        if (pm is null) {
            ActiveModel = "Claude unavailable - plain failover";
            yield return new StatusUpdate(ActiveModel);
            var convo = new Conversation(_services.CreateLoop(gate), new OrchDesk.Core.Accounts.AccountRotator([new AutoChatProvider(models, _services.Usage)]), EffectiveWorkspace);
            if (_conversation is not null) convo.Messages.AddRange(_conversation.Messages);
            _conversation = convo;
            await foreach (var ev in convo.SendAsync(text, ct, WorkModeIndex == 0)) yield return ev;
            yield break;
        }
        var runner = new TeamOrchestrator(() => { var loop = _services.CreateLoop(gate); AttachChanges(loop); return loop; }, _services.Usage, config);

        var item = new TeamItem(); Items.Add(item);
        runner.Changed += job => Dispatch(() => { LastTeamJob = job; item.Refresh(job, _services.Usage); Agents.Update(job); });
        runner.Progress += ev => Dispatch(() => Render(ev));
        ActiveModel = "Auto model ? PM " + pm.ModelId;
        var refresh = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        refresh.Tick += (_, _) => Agents.Tick(); refresh.Start();
        var transcript=string.Join("\n",Items.Select(i=>i is UserItem u?"User: "+u.Text:i is AssistantItem a?"Assistant: "+a.Text:"").Where(t=>t.Length>0));
        TeamJob job;
        try { job = await runner.RunAsync(text+"\nConversation context:\n"+transcript, EffectiveWorkspace, pm, workers.Select(p => config.Workers.TryGetValue(ModelUsageTracker.Key(p), out var tags) ? new TeamWorker(p, tags.Skills, tags.Sizes) : new TeamWorker(p, ["*"], ["*"])).ToList(), WorkModeIndex == 0, _services.Config.Supervisor.TestCommand, ct, verify: !_services.ChatOnly); } finally { refresh.Stop(); }
        LastTeamJob = job;
        if (job.Status == "done" && job.Cards.LastOrDefault(c => c.Role == "PM") is { } summary) yield return new TextDelta(summary.Output);
        if (WorkModeIndex == 0) Items.Add(new PlanItem(job.Cards.Where(c => c.Role == "Worker").Select(c => c.Title).ToList(), RunApprovedPlan));
        yield return new Done();
    }

    private static void Dispatch(Action action) { if (Application.Current is { } app) app.Dispatcher.Invoke(action); else action(); }

    public TeamJob? LastTeamJob { get; private set; }

    private async Task RunApprovedPlan(string task)
    {
        if (IsBusy) return;
        WorkModeIndex = 1;
        Input = task;
        await SendCommand.ExecuteAsync(null);
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
            case UsageReported u:
                _runUsage = _runUsage is null ? u.Usage : new ResponseUsage(_runUsage.InputTokens + u.Usage.InputTokens, _runUsage.OutputTokens + u.Usage.OutputTokens);
                break;
            case StatusUpdate s when s.Text.StartsWith("Using "):
                _runUsage = null;
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
