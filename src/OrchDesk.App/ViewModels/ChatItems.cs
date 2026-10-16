using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OrchDesk.Core.Permissions;

namespace OrchDesk.App.ViewModels;

/// <summary>Base for every row in the chat list; MainWindow picks a DataTemplate by the concrete type.</summary>
public abstract class ChatItem : ObservableObject { public DateTimeOffset Timestamp {get;init;} = DateTimeOffset.Now; }

public sealed class UserItem(string text) : ChatItem
{
    public string Text { get; } = text;
}

public sealed partial class AssistantItem(string modelName) : ChatItem
{
    public string ModelName { get; } = modelName;
    [ObservableProperty] private string _thoughtSummary = "";
    [ObservableProperty] private string _usageTooltip = "";
    public OrchDesk.Core.Models.ResponseUsage? Usage { get; set; }
    public double ThinkingSeconds { get; set; }
    public string? ProviderId { get; init; }
    public OrchDesk.Core.Config.ModelPickerOption Provider => new(ProviderId, ProviderId ?? ModelName);
    [ObservableProperty] private bool _isStreaming;
    [ObservableProperty] private string _reasoning = "";
    [ObservableProperty] private string _text = "";
    public void Append(string delta) => Text += delta;
}

/// <summary>One tool call shown as a single trace line ("Reading Program.cs"); click to expand its output.</summary>
public sealed partial class ToolItem(string callId, string title, string arguments) : ChatItem
{
    private readonly System.Diagnostics.Stopwatch _watch = System.Diagnostics.Stopwatch.StartNew();
    public string Duration => $"{_watch.Elapsed.TotalSeconds:0.0}s";
    public string StateIcon => IsRunning ? "\u25cc" : IsError ? "\u2715" : "\u2713";
    partial void OnIsRunningChanged(bool value) { if (!value) _watch.Stop(); OnPropertyChanged(nameof(Duration)); OnPropertyChanged(nameof(StateIcon)); }
    partial void OnIsErrorChanged(bool value) => OnPropertyChanged(nameof(StateIcon));
    public string CallId { get; } = callId;
    public string Title { get; } = title;
    public string Arguments { get; } = arguments;
    [ObservableProperty] private string? _output;
    [ObservableProperty] private bool _isError;
    [ObservableProperty] private bool _isExpanded;
    [ObservableProperty] private bool _isRunning = true;

    [RelayCommand] private void Toggle() => IsExpanded = !IsExpanded;
}

public sealed class StatusItem(string text) : ChatItem
{
    public string Text { get; } = text;
}

public sealed class ErrorItem(string text) : ChatItem
{
    public string Text { get; } = text;
}

/// <summary>The Approve/Deny card. The agent loop awaits Answer until a button is clicked (or Stop is pressed).</summary>
public sealed partial class ApprovalItem(ApprovalRequest request) : ChatItem
{
    public string? Diff { get; } = request.Diff;
    private readonly TaskCompletionSource<ApprovalAnswer> _tcs = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public string Summary { get; } = request.Summary;
    public string Reason { get; } = request.Reason;
    public Task<ApprovalAnswer> Answer => _tcs.Task;
    [ObservableProperty] private string? _decision;
    public bool IsPending => Decision is null;

    partial void OnDecisionChanged(string? value) => OnPropertyChanged(nameof(IsPending));

    [RelayCommand] private void Approve() => Resolve(ApprovalAnswer.Approve, "Approved");
    [RelayCommand] private void ApproveAlways() => Resolve(ApprovalAnswer.AlwaysThisSession, "Approved for this session");
    [RelayCommand] private void Deny() => Resolve(ApprovalAnswer.Deny, "Denied");

    public void Cancel() => Resolve(ApprovalAnswer.Deny, "Cancelled");

    private void Resolve(ApprovalAnswer answer, string label)
    {
        if (_tcs.TrySetResult(answer)) Decision = label;
    }
}

public sealed partial class PlanStep(string text) : ObservableObject
{
    [ObservableProperty] private string _text = text;
    [ObservableProperty] private string _number = "";
}

public sealed partial class PlanItem : ChatItem
{
    public System.Collections.ObjectModel.ObservableCollection<PlanStep> Steps { get; } = [];
    private readonly Func<string, Task> _run;
    [ObservableProperty, NotifyPropertyChangedFor(nameof(CanApprove))] private bool _isPending = true;
    public bool CanApprove => IsPending && Steps.Any(s => !string.IsNullOrWhiteSpace(s.Text));
    public PlanItem(IEnumerable<string> steps, Func<string, Task> run) {
        _run = run;
        Steps.CollectionChanged += (_, e) => {
            if (e.NewItems is not null) foreach (PlanStep step in e.NewItems) step.PropertyChanged += (_, _) => OnPropertyChanged(nameof(CanApprove));
            for (var i = 0; i < Steps.Count; i++) Steps[i].Number = $"{i + 1}.";
            OnPropertyChanged(nameof(CanApprove));
        };
        foreach (var step in steps) Steps.Add(new(step));
    }
    [RelayCommand] private void AddStep() => Steps.Add(new("New step"));
    [RelayCommand] private void RemoveStep(PlanStep step) => Steps.Remove(step);
    [RelayCommand] private void Cancel() => IsPending = false;
    [RelayCommand] private async Task Approve() { if (!CanApprove) return; IsPending = false; await _run(OrchDesk.Core.Agent.WorkPlan.ApprovedTask(Steps.Select(s => s.Text))); }
}

public sealed partial class ChangeItem(ChangeEntry entry) : ObservableObject
{
    public ChangeEntry Entry { get; } = entry;
    public string Path => Entry.Path;
    public string Action => Entry.Action;
    public string Diff => Entry.Diff;
    public bool CanUndo => Entry.CanUndo;
    [ObservableProperty] private string _error = "";
    [RelayCommand] private void Undo() { try { Entry.Undo(); OnPropertyChanged(nameof(CanUndo)); } catch (Exception ex) { Error = ex.Message; } }
}

public sealed class TeamItem : ChatItem
{
    public OrchDesk.Core.Orchestration.TeamJob? Job { get; private set; }
    public string Header { get; private set; } = "Auto model \u00b7 Team";
    public string ThoughtSummary { get; private set; } = "";
    public IReadOnlyList<ModelUsageRow> ModelUsageRows { get; private set; } = [];
    public string ModelUsage { get; private set; } = "";
    public string UsageTooltip { get; private set; } = "";
    public System.Collections.ObjectModel.ObservableCollection<OrchDesk.Core.Orchestration.TeamCard> Cards { get; } = [];
    public void Refresh(OrchDesk.Core.Orchestration.TeamJob job, OrchDesk.Core.Orchestration.ModelUsageTracker usage)
    {
        Job = job;
        Header = $"Auto model \u00b7 PM {job.Pm} \u00b7 {job.Status}";
        ThoughtSummary = job.ThoughtSummary;
        UsageTooltip = job.Usage.Tooltip("Team", string.Join(", ", job.Cards.Select(c => c.Model).Where(m=>m.Length>0).Distinct()));
        Cards.Clear(); foreach (var card in job.Cards.ToArray()) Cards.Add(card);
        ModelUsageRows = ModelUsageRow.Read(usage);
        OnPropertyChanged(nameof(ModelUsageRows));
        ModelUsage = string.Join("\n", usage.Snapshot().Select(p => $"{p.Key}   {p.Value.Requests} requests \u00b7 {p.Value.Tokens:N0} tokens today; session {p.Value.SessionRequests} requests / {p.Value.SessionTokens:N0} tokens" + (p.Value.BackoffUntil > DateTimeOffset.UtcNow ? $" \u00b7 backoff until {p.Value.BackoffUntil:HH:mm}" : " \u00b7 ready")));
        OnPropertyChanged(nameof(Header)); OnPropertyChanged(nameof(ThoughtSummary)); OnPropertyChanged(nameof(ModelUsage)); OnPropertyChanged(nameof(UsageTooltip));
    }
}
