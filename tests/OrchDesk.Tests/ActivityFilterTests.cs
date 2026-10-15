using OrchDesk.Core.Activity;
using OrchDesk.Core.Filtering;
namespace OrchDesk.Tests;

public class ActivityFilterTests
{
    [Fact]
    public void ReadsCheckpointFlatRunAndMorningReportWithoutCountingProgressAsRun()
    {
        using var root = new TempDir();
        root.File("runs/one/checkpoint.json", "{\"startedAt\":\"2026-10-13T12:00:00Z\"}");
        root.File("runs/one/progress.log", "2026-10-13T12:00:00Z Started\n2026-10-14T12:00:00Z Finished");
        root.File("runs/two.json", "{\"startedAt\":\"2026-10-14T12:00:00Z\"}");
        root.File("reports/overnight-2026-10-14.md", "Ran 01:00-03:00 · 3 task(s) · 3 succeeded");
        var days = ActivityAggregator.Read(root.Path, new DateOnly(2026, 10, 15));
        Assert.Equal(5, days.Sum(d => d.Runs)); Assert.Equal(1, days.Single(d => d.Date == new DateOnly(2026, 10, 13)).Runs);
    }
    [Theory][InlineData(0, 0)][InlineData(1, 1)][InlineData(3, 2)][InlineData(7, 3)][InlineData(15, 4)] public void FiveIntensityLevels(int total, int level) => Assert.Equal(level, new ActivityDay(new DateOnly(2026, 10, 15), total, 0).Level);
    [Fact] public void EmptyHistoryAndDateBounds() { using var root = new TempDir(); root.File("history/a.json", "{\"messages\":[{\"timestamp\":\"2026-10-16T12:00:00Z\"},{\"timestamp\":\"2025-01-01T12:00:00Z\"}]}"); var days = ActivityAggregator.Read(root.Path, new DateOnly(2026, 10, 15)); Assert.All(days, d => Assert.Equal(0, d.Total)); Assert.Throws<ArgumentOutOfRangeException>(() => ActivityAggregator.Read(root.Path, new DateOnly(2026, 10, 15), 0)); }
    [Fact] public void EmptyFilterAcceptsEverythingAndNullDoesNotContain() { Assert.True(FilterModel.BuildPredicate<string>([], (s, _) => s)("anything")); Assert.False(new FilterModel("name", FilterOperator.Contains, "x").Matches(null)); }
    [Fact] public void AggregatesDatesAndSkipsBrokenFiles() { var root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString()); Directory.CreateDirectory(Path.Combine(root, "history")); try { File.WriteAllText(Path.Combine(root, "history", "a.json"), "{\"messages\":[{\"timestamp\":\"2026-10-14T12:00:00Z\"},{\"timestamp\":\"2026-10-14T13:00:00Z\"},{\"timestamp\":\"2020-01-01T12:00:00Z\"}]}"); File.WriteAllText(Path.Combine(root, "history", "broken.json"), "{"); var days = ActivityAggregator.Read(root, new DateOnly(2026, 10, 15)); Assert.Equal(182, days.Count); Assert.Equal(2, days.Sum(d => d.Messages)); Assert.Equal(2, days.Single(d => d.Date == new DateOnly(2026, 10, 14)).Messages); } finally { Directory.Delete(root, true); } }
    [Theory][InlineData(FilterOperator.Is, "SONNET", true)][InlineData(FilterOperator.IsNot, "Sonnet", false)][InlineData(FilterOperator.Contains, "Claude Sonnet", true)] public void Operators(FilterOperator op, string value, bool expected) => Assert.Equal(expected, new FilterModel("model", op, "sonnet").Matches(value));
    [Fact] public void CombinesAllFilters() { var predicate = FilterModel.BuildPredicate<string>([new("name", FilterOperator.Contains, "son"), new("name", FilterOperator.IsNot, "son")], (s, _) => s); Assert.True(predicate("Sonnet")); Assert.False(predicate("son")); Assert.False(predicate("Opus")); }
}
