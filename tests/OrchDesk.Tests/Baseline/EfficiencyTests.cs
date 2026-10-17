using System.Runtime.CompilerServices;
using OrchDesk.Core.Config;
using OrchDesk.Core.Automation;
using OrchDesk.Core.Knowledge;
using OrchDesk.Core.Telemetry;
using OrchDesk.Core.Dashboard;
using OrchDesk.Core.Research;
using OrchDesk.Core.Models;
using OrchDesk.Core.Tools;
using OrchDesk.Core.Permissions;
using OrchDesk.Core.Safety;
using OrchDesk.Core.Orchestration;
using OrchDesk.Core.Providers;
using Xunit;

namespace OrchDesk.Tests.Baseline;

public class EfficiencyTests
{

    private static string Dir() { var d = Path.Combine(Path.GetTempPath(), "orch-eff-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(d); return d; }

    private static string Project()
    {
        var d = Dir(); Directory.CreateDirectory(Path.Combine(d, "src")); Directory.CreateDirectory(Path.Combine(d, "bin")); Directory.CreateDirectory(Path.Combine(d, ".git"));
        File.WriteAllText(Path.Combine(d, "src", "Widget.cs"), "public class Widget\n{\n    public void Spin() { }\n}\n");
        File.WriteAllText(Path.Combine(d, "src", "Gear.cs"), "public class Gear { Widget w; void Turn() { w.Spin(); } }");
        File.WriteAllText(Path.Combine(d, "src", "Panel.cs"), "public class Panel { Widget a; Widget b; void Draw() { a.Spin(); b.Spin(); } }");
        File.WriteAllText(Path.Combine(d, "bin", "Skip.cs"), "public class Skipped { }");
        File.WriteAllText(Path.Combine(d, ".git", "Hidden.cs"), "public class Hidden { }");
        return d;
    }

    [Fact]
    public void RepoMapRanksTheMostReferencedFileFirstAndSkipsBuildFolders()
    {
        var map = RepoMap.Build(Project());
        Assert.Equal(3, map.FileCount);
        Assert.DoesNotContain(map.Files, f => f.Path.Contains("Skip") || f.Path.Contains("Hidden"));
        Assert.True(map.RankOf("src/Widget.cs") > map.RankOf("src/Gear.cs"));
        var text = map.Render(4000);
        Assert.True(text.IndexOf("Widget.cs", StringComparison.Ordinal) < text.IndexOf("Gear.cs", StringComparison.Ordinal));
        Assert.Contains("Spin", text);
    }

    [Fact]
    public void RepoMapHonoursTheBudgetAndFocusFindsMentionedFiles()
    {
        var map = RepoMap.Build(Project());
        Assert.True(map.Render(150).Length <= 150);
        Assert.Equal("", RepoMap.Build(Dir()).Render(1000));
        Assert.Equal("src/Gear.cs", map.Focus("please fix the Gear class", 1).Single());
        Assert.Empty(map.Focus("nothing relevant here"));
    }

    [Fact]
    public void RoutingSuggestsCheaperTierThatKeepsPassingAndSkipsOneThatKeepsFailing()
    {
        var stats = new RoutingStats(); var tiers = new[] { "T0", "T1", "T2" }; var bucket = RoutingStats.Bucket("code", false);
        Assert.Equal(2, stats.Suggest(bucket, tiers, 2).Start);
        for (int i = 0; i < 5; i++) stats.Record(bucket, "T0", true);
        Assert.Equal(0, stats.Suggest(bucket, tiers, 2).Start);
        for (int i = 0; i < 4; i++) stats.Record(bucket, "T1", false);
        Assert.Equal(0, stats.Suggest(bucket, tiers, 1).Start);
        var other = RoutingStats.Bucket("code", true); for (int i = 0; i < 4; i++) stats.Record(other, "T1", false);
        Assert.Equal(2, stats.Suggest(other, tiers, 1).Start);
    }

    [Fact]
    public void RoutingStatsSurviveSaveAndLoadAndCorruptFilesAreIgnored()
    {
        var path = Path.Combine(Dir(), "stats.json"); var a = new RoutingStats(path); a.Record("code:easy", "T0", true); a.Record("code:easy", "T0", false);
        Assert.Equal((2, 1), new RoutingStats(path).Get("code:easy", "T0"));
        File.WriteAllText(path, "not json"); Assert.Equal((0, 0), new RoutingStats(path).Get("code:easy", "T0"));
    }


    [Fact]
    public void NightLadderUsesLocalModelsSmallestFirstThenClaudeOnly()
    {
        var ladder = LegacyDefaults.Ladder();
        var night = NightLadderBuilder.Build(["text-embedding-nomic", "qwen3-30b-a3b", "gemma-3-4b", "llama-8b-instruct"], ladder);
        Assert.Equal(["gemma-3-4b", "llama-8b-instruct", "qwen3-30b-a3b"], night.Where(t => t.Provider == "local").Select(t => t.Model));
        Assert.Equal(["claude", "claude"], night.Skip(3).Select(t => t.Provider));
        Assert.DoesNotContain(night, t => t.Provider.StartsWith("opencode"));
        Assert.Equal(4, NightLadderBuilder.Build(Enumerable.Range(1, 12).Select(i => $"m-{i}b"), ladder).Count(t => t.Provider == "local"));
        var config = new AppConfig { NightShift = true, NightLadder = night }; Assert.Same(night, config.ActiveLadder);
        config.NightShift = false; Assert.Same(config.Ladder, config.ActiveLadder);
        Assert.Equal(new AppConfig().Ladder.Count, new AppConfig { NightShift = true }.ActiveLadder.Count);
    }


    [Fact]
    public void UsageLogBucketsByDayComputesStreakAndSurvivesReload()
    {
        var path = Path.Combine(Dir(), "usage.jsonl"); var log = new UsageLog(path); var today = new DateOnly(2026, 10, 17);
        DateTime At(int back) => today.AddDays(-back).ToDateTime(new TimeOnly(12, 0), DateTimeKind.Local).ToUniversalTime();
        log.Record(new(At(0), "Opus", "plan", 100, 50)); log.Record(new(At(0), "Haiku", "work", 10, 5)); log.Record(new(At(1), "Opus", "plan", 20, 0)); log.Record(new(At(5), "Opus", "plan", 1000, 0));
        var daily = log.Daily(7, today);
        Assert.Equal(7, daily.Count); Assert.Equal(165, daily[6].Tokens); Assert.Equal(2, daily[6].Calls); Assert.Equal(0, daily[3].Tokens);
        var summary = log.Summarize(7, today);
        Assert.Equal(2, summary.Streak); Assert.Equal(1185, summary.Tokens); Assert.Equal(new DateOnly(2026, 10, 12), summary.BusiestDay!.Date);
        Assert.Equal("Opus", summary.ByModel[0].Model);
        File.AppendAllText(path, "garbage\n");
        Assert.Equal(4, new UsageLog(path).Count);
        Assert.Equal(0, UsageLog.Estimate("")); Assert.Equal(3, UsageLog.Estimate("abcdefghij"));
    }

}
