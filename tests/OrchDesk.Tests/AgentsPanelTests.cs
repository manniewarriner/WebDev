using OrchDesk.Core.Config;
using OrchDesk.Core.Models;
using OrchDesk.Core.Orchestration;
using OrchDesk.Core.Presentation;
namespace OrchDesk.Tests;
public sealed class AgentsPanelTests
{
    [Theory]
    [InlineData(1440, true, false)]
    [InlineData(1024, false, false)]
    [InlineData(699, false, true)]
    [InlineData(1100, true, false)]
    public void Responsive_defaults(double width, bool open, bool drawer) {
        var state = new AgentsPanelState(); state.Resize(width);
        Assert.Equal(open, state.IsOpen); Assert.Equal(drawer, state.IsDrawer);
    }
    [Fact]
    public void Explicit_collapse_survives_resize_and_config_roundtrip() {
        using var dir = new TempDir(); var store = new ConfigStore(dir.Path);
        var state = new AgentsPanelState(); state.Toggle();
        store.Save(new AppConfig { AgentsPanelOpen = state.Preference });
        var restored = new AgentsPanelState(store.Load().AgentsPanelOpen); restored.Resize(1440);
        Assert.False(restored.IsOpen); restored.Resize(600); Assert.False(restored.IsOpen);
        restored.Toggle(); restored.Resize(1024); Assert.True(restored.IsOpen);
    }
    [Fact]
    public void Planning_transitions_and_totals_include_live_elapsed() {
        var now = DateTimeOffset.UtcNow;
        var job = new TeamJob();
        job.Cards.Add(new() { Role="PM", Usage=new(100,20), Elapsed=TimeSpan.FromSeconds(4) });
        job.Cards.Add(new() { Usage=new(200,30,true), Elapsed=TimeSpan.FromSeconds(2), Started=now.AddSeconds(-3) });
        Assert.Equal("planning", AgentsPanelState.PlanningStatus(job));
        job.Status="running"; Assert.Equal("waiting", AgentsPanelState.PlanningStatus(job));
        job.Cards[0].Started=now; Assert.Equal("reviewing", AgentsPanelState.PlanningStatus(job));
        Assert.Equal(350, job.Usage.InputTokens + job.Usage.OutputTokens); Assert.True(job.Usage.Estimated);
        Assert.Equal(TimeSpan.FromSeconds(9), AgentsPanelState.TotalTime(job,now));
        job.Cards[0].Started=null; job.Status="done"; Assert.Equal("done", AgentsPanelState.PlanningStatus(job));
    }
}
