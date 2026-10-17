using System.Net;
using System.Text.Json;
using OrchDesk.Core.Automation;
using OrchDesk.Core.Config;
using Xunit;

namespace OrchDesk.Tests;

public class TelegramJobTests
{
    private static JobFinished Job(string result = "Done", string summary = "Implemented changes.\nTests passed.") => new("job-1", "  Example goal  ", @"C:\work\Project", result, TimeSpan.FromSeconds(65), summary);

    [Theory]
    [InlineData("Done")]
    [InlineData("Failed")]
    [InlineData("Stopped")]
    [InlineData("Usage limit")]
    public void NotificationIncludesOutcomeAndContext(string outcome)
    {
        var text = JobMessageBuilder.Build(Job(outcome));
        Assert.StartsWith("Example goal\n", text);
        Assert.Contains("Project", text); Assert.Contains(outcome + " · 00:01:05", text);
        Assert.Contains("Tests passed.", text);
    }

    [Fact]
    public void NotificationRedactsBeforeTruncatingAndLimitsSummary()
    {
        var text = JobMessageBuilder.Build(Job(summary: "token=hidden\nsk-abcdef123456\nthird\nfourth\nfifth"));
        Assert.DoesNotContain("hidden", text); Assert.DoesNotContain("abcdef", text); Assert.DoesNotContain("fifth", text);
        Assert.True(JobMessageBuilder.Build(Job(summary: new string('x', 12000))).Length <= 3500);
        Assert.DoesNotContain("private-value", JobMessageBuilder.Build(Job(summary: "private-value"), s => s.Replace("private-value", "[REDACTED]")));
    }

    [Fact]
    public async Task NotificationDeduplicatesConcurrentCompletions()
    {
        var count = 0;
        var c = new AppConfig { TelegramEnabled = true, TelegramChatId = 42 };
        var notifier = new JobNotifier(() => c, () => true, (id, text, ct) => { Assert.Equal(42, id); Interlocked.Increment(ref count); return Task.CompletedTask; }, _ => { });
        await Task.WhenAll(Enumerable.Range(0, 20).Select(_ => notifier.Notify(Job())));
        Assert.Equal(1, count);
    }

    [Theory]
    [InlineData(false, true, 42, true)]
    [InlineData(true, false, 42, true)]
    [InlineData(true, true, 0, true)]
    [InlineData(true, true, 42, false)]
    public async Task DisabledUnpairedOrMissingTokenNeverSends(bool enabled, bool toggle, long chat, bool token)
    {
        var count = 0;
        var c = new AppConfig { TelegramEnabled = enabled, TelegramNotifyJobDone = toggle, TelegramChatId = chat };
        var notifier = new JobNotifier(() => c, () => token, (_, _, _) => { count++; return Task.CompletedTask; }, _ => { });
        await notifier.Notify(Job()); Assert.Equal(0, count);
    }

    [Fact]
    public async Task DeliveryFailureIsLoggedWithoutExceptionSecrets()
    {
        var logs = new List<string>();
        var notifier = new JobNotifier(() => new() { TelegramEnabled = true, TelegramChatId = 42 }, () => true,
            (_, _, _) => throw new Exception("secret-token"), logs.Add);
        await notifier.Notify(Job()); Assert.Single(logs); Assert.DoesNotContain("secret-token", logs[0]);
    }

    [Fact]
    public async Task DeliveryRunsInBackgroundAndTimesOutEvenWhenSenderIgnoresCancellation()
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var blocked = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var logs = new List<string>();
        var notifier = new JobNotifier(() => new() { TelegramEnabled = true, TelegramChatId = 42 }, () => true,
            (_, _, _) => { entered.TrySetResult(); return blocked.Task; }, logs.Add, deliveryTimeout: TimeSpan.FromMilliseconds(100));
        var delivery = notifier.Notify(Job());
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await delivery.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.False(blocked.Task.IsCompleted); Assert.Single(logs);
        blocked.TrySetResult();
    }

    [Fact]
    public void TruncationPreservesUnicodePairs()
    {
        var text = JobMessageBuilder.Clip("abc" + char.ConvertFromUtf32(0x1F600) + "tail", 5);
        Assert.Equal("abc…", text);
    }

    [Theory]
    [InlineData("/status", true)]
    [InlineData(" STATUS ", true)]
    [InlineData("What's Happening", true)]
    [InlineData("whats happening", true)]
    [InlineData("status please", false)]
    [InlineData("start work", false)]
    public void CommandMatching(string text, bool matches) => Assert.Equal(matches, StatusMessageBuilder.IsStatusCommand(text));

    [Fact]
    public void IdleAndBusyStatusIncludeDetailsAndRespectCap()
    {
        var now = DateTimeOffset.UtcNow;
        Assert.StartsWith("Idle", StatusMessageBuilder.Build(new(), now));
        var s = new StatusSnapshot(true, "Build dashboard", "Project", now.AddMinutes(-2), ["PM opus: planning", "Worker sonnet: working"],
            "Add tests", "Verify", 2, 4, ["one", "two", "three", "four", "five", "six"], ["old", "error1", "error2", "error3"], "Running, round 3, next run: tomorrow");
        var text = StatusMessageBuilder.Build(s, now);
        foreach (var value in new[] { "Busy", "Build dashboard", "00:02:00", "PM opus: planning", "Worker sonnet: working", "Add tests", "Verify", "2/4", "round 3", "next run", "six", "error3" }) Assert.Contains(value, text);
        Assert.DoesNotContain("\none\n", text); Assert.DoesNotContain("\nold\n", text);
        Assert.True(StatusMessageBuilder.Build(s with { Name = new string('x', 9000), Agents = Enumerable.Repeat(new string('x', 9000), 100).ToArray(), Output = Enumerable.Repeat(new string('x', 9000), 10).ToArray() }, now).Length <= 3500);
        Assert.DoesNotContain("secret-value", StatusMessageBuilder.Build(s with { Name = "secret-value" }, now, x => x.Replace("secret-value", "[REDACTED]")));
    }

    [Fact]
    public void FileFallbackReadsWithoutChangingFiles()
    {
        var dir = Path.Combine(Path.GetTempPath(), "orch-status-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(dir, "runs"));
        try
        {
            var state = JsonSerializer.Serialize(new[] { new SupervisorTask { Goal = "Test goal", Project = "Project", Status = QueueTaskState.Running, Step = 2, Runs = 3, ResetAt = DateTimeOffset.UtcNow.AddMinutes(10) } });
            File.WriteAllText(Path.Combine(dir, "supervisor-state.json"), state);
            var log = "{\"at\":\"2026-10-17T12:00:00Z\",\"kind\":\"working\",\"Role\":\"Worker\",\"Tier\":\"sonnet\",\"text\":\"latest output\"}\ninvalid\n{\"kind\":\"ErrorEvent\",\"text\":\"recent error\"}\n";
            var path = Path.Combine(dir, "runs", "sample.jsonl"); File.WriteAllText(path, log);
            var text = StatusMessageBuilder.Build(StatusMessageBuilder.FromFiles(dir), DateTimeOffset.UtcNow);
            foreach (var value in new[] { "Busy", "Test goal", "round 3", "sonnet", "latest output", "recent error" }) Assert.Contains(value, text);
            Assert.Equal(log, File.ReadAllText(path)); Assert.Equal(state, File.ReadAllText(Path.Combine(dir, "supervisor-state.json")));
        }
        finally { Directory.Delete(dir, true); }
    }

    private sealed class Host : IRemoteHost
    {
        public int Reads, Runs, Stops;
        public bool Busy => false;
        public string StatusText() { Reads++; return "Idle"; }
        public Task<string> RunAsync(string text, bool planOnly, CancellationToken ct) { Runs++; return Task.FromResult("done"); }
        public void Stop() => Stops++;
        public string Resolve(bool approve, string? id) => throw new InvalidOperationException();
    }
    private sealed class Sender : HttpMessageHandler
    {
        public int Sends;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) { Sends++; return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)); }
    }
    [Theory]
    [InlineData("/status")]
    [InlineData(" status ")]
    [InlineData("what's happening")]
    [InlineData("WHATS HAPPENING")]
    public async Task OnlyPairedChatCanReadStatusAndAliasesNeverRunWork(string command)
    {
        var sender = new Sender(); using var http = new HttpClient(sender); var host = new Host();
        var channel = new TelegramChannel(http, "fake", new() { TelegramChatId = 42 }, host, _ => { });
        JsonElement Update(long id) => JsonSerializer.SerializeToElement(new { message = new { chat = new { id, type = "private" }, text = command } });
        await channel.HandleAsync(Update(99), default); Assert.Equal(0, sender.Sends); Assert.Equal(0, host.Reads);
        await channel.HandleAsync(Update(42), default); Assert.Equal(1, sender.Sends); Assert.Equal(1, host.Reads); Assert.Equal(0, host.Runs); Assert.Equal(0, host.Stops);
    }

    [Fact]
    public void DefaultToggleAndConfigRoundTripPreserveUnknownKeys()
    {
        Assert.True(new AppConfig().TelegramNotifyJobDone);
        var c = JsonSerializer.Deserialize<AppConfig>("{\"TelegramNotifyJobDone\":false,\"unrelated\":123}")!;
        Assert.False(c.TelegramNotifyJobDone);
        using var saved = JsonDocument.Parse(JsonSerializer.Serialize(c)); Assert.Equal(123, saved.RootElement.GetProperty("unrelated").GetInt32());
    }
}
