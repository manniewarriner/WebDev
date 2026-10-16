using OrchDesk.Core.Orchestration;

namespace OrchDesk.Core.Presentation;

public sealed class AgentsPanelState(bool? open = null)
{
    public bool? Preference { get; private set; } = open;
    public double Width { get; private set; } = 1180;
    public bool IsDrawer => Width < 700;
    public bool IsOpen => Preference ?? Width >= 1100;
    public void Resize(double width) => Width = width;
    public void Toggle() => Preference = !IsOpen;
    public static string PlanningStatus(TeamJob job) => job.Status switch {
        "planning" => "planning", "done" => "done", "running" when job.SnapshotCards().Any(c => c.Role == "PM" && c.Started != null) => "reviewing", _ => "waiting"
    };
    public static TimeSpan Elapsed(TeamCard card, DateTimeOffset now) => card.Elapsed + (card.Started is {} start ? now - start : TimeSpan.Zero);
    public static TimeSpan TotalTime(TeamJob job, DateTimeOffset now) => TimeSpan.FromTicks(job.SnapshotCards().Sum(c => Elapsed(c, now).Ticks));
}
