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
