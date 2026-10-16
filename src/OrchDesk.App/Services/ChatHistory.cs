using OrchDesk.App.ViewModels;
using OrchDesk.Core.Models;
using OrchDesk.Core.Orchestration;
namespace OrchDesk.App.Services;

public sealed record SavedTeam(string PmProvider, string Pm, string Status, TeamCard[] Cards, TeamFailure[] Failures, TeamDecision[] Decisions, string[] WorkerSkips)
{
    public static SavedTeam From(TeamJob job) => new(job.PmProvider, job.Pm, job.Status, job.SnapshotCards(), job.SnapshotFailures(), job.SnapshotDecisions(), job.WorkerSkips.ToArray());
    public TeamJob Restore() {
        var job = new TeamJob { PmProvider = PmProvider, Pm = Pm, Status = Status };
        job.Cards.AddRange(Cards); job.Failures.AddRange(Failures); job.Decisions.AddRange(Decisions); job.WorkerSkips.AddRange(WorkerSkips);
        return job;
    }
}
public sealed record SavedMessage(string Role, string Text, DateTimeOffset Timestamp, ResponseUsage? Usage = null, double ThinkingSeconds = 0, string ThoughtSummary = "", string UsageTooltip = "", string? AnsweringProvider = null, SavedTeam? Team = null);
public sealed record SavedChat(string Title, string Model, DateTimeOffset Date, SavedMessage[] Messages, string? Workspace = null, string? ProviderId = null);
public static class ChatHistory
{
    public static SavedMessage[] SerializeItems(IEnumerable<ChatItem> items) => items.Where(i => i is UserItem or AssistantItem || i is TeamItem { Job: not null }).Select(i => new SavedMessage(i is UserItem ? "user" : i is TeamItem ? "team" : "assistant", i is UserItem u ? u.Text : (i as AssistantItem)?.Text ?? "", i.Timestamp, (i as AssistantItem)?.Usage, (i as AssistantItem)?.ThinkingSeconds ?? 0, (i as AssistantItem)?.ThoughtSummary ?? "", (i as AssistantItem)?.UsageTooltip ?? "", (i as AssistantItem)?.ProviderId, i is TeamItem { Job: { } job } ? SavedTeam.From(job) : null)).ToArray();
    public static ChatItem[] RestoreItems(SavedMessage[] messages, string model, ModelUsageTracker usage) => messages.Select(m => {
        if (m.Team is { } saved) { var item = new TeamItem { Timestamp = m.Timestamp }; item.Refresh(saved.Restore(), usage); return (ChatItem)item; }
        return m.Role == "user" ? (ChatItem)new UserItem(m.Text) { Timestamp = m.Timestamp } : new AssistantItem(model) { Text = m.Text, Timestamp = m.Timestamp, Usage = m.Usage, ThinkingSeconds = m.ThinkingSeconds, ThoughtSummary = m.ThoughtSummary, UsageTooltip = m.UsageTooltip, ProviderId = m.AnsweringProvider };
    }).ToArray();
}
