using OrchDesk.App.ViewModels;
using OrchDesk.Core.Orchestration;
using OrchDesk.Core.Providers;
namespace OrchDesk.App.Services;

public sealed record SendOutcome(string? Provider, string? Model, string? Manager, string Reply, string Error)
{
    public int ExitCode => Error.Length == 0 ? 0 : 1;
    public static SendOutcome Create(TeamJob? team, IModelProvider? provider, IEnumerable<ChatItem> items) {
        var reply=team is {Status:"done"} ? team.Summary : string.Concat(items.OfType<AssistantItem>().Select(a=>a.Text));
        var error=team is {Status:"done"} ? "" : string.Join("; ",items.OfType<ErrorItem>().Select(e=>e.Text));
        if(error.Length==0 && string.IsNullOrWhiteSpace(reply)) error="Send failed: provider returned an empty reply.";
        return new(team?.PmProvider ?? provider?.Id,team?.Pm ?? provider?.ModelId,team?.Manager,reply,error);
    }
}
