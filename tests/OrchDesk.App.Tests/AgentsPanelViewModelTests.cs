using OrchDesk.App.ViewModels;
using OrchDesk.Core.Orchestration;
namespace OrchDesk.Tests;
public sealed class AgentsPanelViewModelTests
{
    [Fact]
    public void Events_update_rows_in_place_and_reset_clears_team() {
        var vm=new AgentsPanelViewModel(); Assert.False(vm.HasTeam);
        var job=new TeamJob { Pm="Claude Opus" };
        var card=new TeamCard { Id="one", Title="Build UI", Model="Kimi", Status="queued", Usage=new(10,20,true) };
        job.Cards.Add(card); vm.Update(job); var row=Assert.Single(vm.Workers);
        foreach(var status in new[] { "running", "review", "backoff", "done", "failed" }) {
            card.Status=status; vm.Update(job); Assert.Same(row,Assert.Single(vm.Workers)); Assert.Equal(status,row.Status);
        }
        job.Decisions.Add("Opus: reassigned Build UI from Qwen to GLM - context overflow"); vm.Update(job); Assert.Contains("context overflow", vm.Decisions);
        Assert.Contains("~30 tokens",vm.Totals); Assert.Equal("1",vm.Count);
        vm.Update(null); Assert.False(vm.HasTeam); Assert.Empty(vm.Workers);
    }
}
