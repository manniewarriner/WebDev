using CommunityToolkit.Mvvm.ComponentModel;
using System.Collections.ObjectModel;
using OrchDesk.Core.Orchestration;
using OrchDesk.Core.Presentation;

namespace OrchDesk.App.ViewModels;

public sealed class AgentRow(TeamCard card) : ObservableObject
{
    public TeamCard Card { get; } = card;
    public string Model => string.IsNullOrEmpty(Card.Model) ? "Auto model" : Card.Model;
    public string Title => Card.Title;
    public string Status => Card.Status;
    public bool IsRunning => Card.Started != null || Status is "running" or "review";
    public string Mark => Model.Contains("Claude", StringComparison.OrdinalIgnoreCase) ? "\u2733" : Model.Contains("Kimi", StringComparison.OrdinalIgnoreCase) ? "K" : "\u25c8";
    public string Detail => $"{AgentsPanelState.Elapsed(Card, DateTimeOffset.UtcNow):m\\:ss} · {(Card.Usage.Estimated ? "~" : "")}{Card.Usage.InputTokens + Card.Usage.OutputTokens:N0} tokens";
    public void Refresh() { foreach (var name in new[] { nameof(Model), nameof(Status), nameof(IsRunning), nameof(Mark), nameof(Detail) }) OnPropertyChanged(name); }
}
public sealed class AgentsPanelViewModel : ObservableObject
{
    private TeamJob? _job;
    public ObservableCollection<AgentRow> Workers { get; } = [];
    public bool HasTeam => _job != null;
    public string Failures => string.Join("\n", _job?.SnapshotFailures().Select(f=>$"{f.Model}: {f.Chunk} - {f.Reason}") ?? []);
    public string Decisions => string.Join("\n", _job?.SnapshotDecisions().Select(d=>d.ToString()) ?? []);
    public string Count => Workers.Count.ToString();
    public string Planning => _job == null ? "" : $"{_job.Pm} · {AgentsPanelState.PlanningStatus(_job)}";
    public string PlanningElapsed => _job == null ? "" : $"{TimeSpan.FromTicks(_job.SnapshotCards().Where(c => c.Role == "PM").Sum(c => AgentsPanelState.Elapsed(c, DateTimeOffset.UtcNow).Ticks)):m\\:ss}";
    public string Totals => _job == null ? "" : $"{(_job.Usage.Estimated ? "~" : "")}{_job.Usage.InputTokens + _job.Usage.OutputTokens:N0} tokens · {AgentsPanelState.TotalTime(_job, DateTimeOffset.UtcNow):m\\:ss}";
    public void Update(TeamJob? job) {
        _job = job;
        var cards = job?.SnapshotCards().Where(c => c.Role != "PM").ToArray() ?? [];
        if (!Workers.Select(r => r.Card).SequenceEqual(cards)) { Workers.Clear(); foreach (var card in cards) Workers.Add(new(card)); }
        Tick(); OnPropertyChanged(nameof(HasTeam)); OnPropertyChanged(nameof(Count));
    }
    public void Tick() { foreach (var row in Workers) row.Refresh(); OnPropertyChanged(nameof(Planning)); OnPropertyChanged(nameof(PlanningElapsed)); OnPropertyChanged(nameof(Totals)); OnPropertyChanged(nameof(Decisions)); OnPropertyChanged(nameof(Failures)); }
}
