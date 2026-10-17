using System.Collections.Concurrent;
using System.Text.Json;
using OrchDesk.Core.Config;
using OrchDesk.Core.Safety;
using OrchDesk.Core.Telemetry;

namespace OrchDesk.Core.Automation;

public interface IJobNotificationHost { }

public sealed record JobFinished(string Id, string Name, string Project, string Result, TimeSpan Duration, string Summary);

public static class JobMessageBuilder
{
    public static string Clip(string text, int max)
    {
        if (text.Length <= max) return text;
        var end = max - 1;
        if (end > 0 && char.IsHighSurrogate(text[end - 1])) end--;
        return text[..end] + "…";
    }
    public static string Build(JobFinished job, Func<string, string>? redact = null)
    {
        var clean = redact ?? Redaction.Clean;
        var lines = new List<string>();
        foreach (var paragraph in clean(job.Summary).Replace("\r", "").Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var remaining = paragraph;
            while (remaining.Length > 320 && lines.Count < 3)
            {
                var end = remaining.LastIndexOf(' ', 320);
                if (end < 80) end = 320;
                if (char.IsHighSurrogate(remaining[end - 1])) end--;
                lines.Add(remaining[..end]); remaining = remaining[end..].TrimStart();
            }
            lines.Add(Clip(remaining, 320));
            if (lines.Count >= 4) break;
        }
        if (lines.Count == 0) lines.Add("No summary available.");
        if (lines.Count == 1) lines.Add("Open OrchDesk for details.");
        return Clip($"{Clip(clean(job.Name).Trim(), 180)}\nProject: {Clip(clean(System.IO.Path.GetFileName(job.Project.TrimEnd('\\', '/'))), 120)}\n{job.Result} · {job.Duration:hh\\:mm\\:ss}\n{Clip(string.Join('\n', lines), 1600)}", 3500);
    }
}

/// <summary>Deduplicates completion events; all delivery work is isolated from the job.</summary>
public sealed class JobNotifier(Func<AppConfig> config, Func<bool> hasToken, Func<long, string, CancellationToken, Task> send,
    Action<string> log, Func<string, string>? redact = null, TimeSpan? deliveryTimeout = null)
{
    private readonly ConcurrentDictionary<string, byte> sent = new();
    public Task Notify(JobFinished job)
    {
        return Task.Run(async () => {
            try
            {
                var c = config();
                if (!c.TelegramEnabled || !c.TelegramNotifyJobDone || c.TelegramChatId == 0 || !hasToken() || !sent.TryAdd(job.Id, 0)) return;
                using var timeout = new CancellationTokenSource(deliveryTimeout ?? TimeSpan.FromSeconds(10));
                await send(c.TelegramChatId, JobMessageBuilder.Build(job, redact), timeout.Token).WaitAsync(timeout.Token);
            }
            catch (Exception) { try { log("Telegram job notification failed (delivery unavailable or timed out)."); } catch { } }
        });
    }
}

public sealed record StatusSnapshot(bool Busy = false, string Name = "", string Project = "", DateTimeOffset? Started = null,
    string[]? Agents = null, string Task = "", string Step = "", int Done = 0, int Total = 0,
    string[]? Output = null, string[]? Errors = null, string Loop = "Idle");

[System.Runtime.Versioning.SupportedOSPlatform("windows")]
public static class TelegramJobDelivery
{
    public static JobNotifier Create(Func<AppConfig> config, SecretStore secrets, System.Net.Http.HttpClient http, string directory, Func<bool>? enabled = null) =>
        new(config, () => (enabled?.Invoke() ?? true) && secrets.Has("telegram"), async (chat, text, ct) => {
            var token = secrets.Get("telegram");
            using var response = await http.PostAsync($"https://api.telegram.org/bot{token}/sendMessage",
                System.Net.Http.Json.JsonContent.Create(new { chat_id = chat, text, disable_web_page_preview = true }), ct);
            response.EnsureSuccessStatusCode();
        }, message => {
            try { File.AppendAllText(System.IO.Path.Combine(directory, "telegram-notifications.log"), DateTimeOffset.UtcNow.ToString("O") + " " + message + "\n"); }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
        }, secrets.Redact);
}

public static class StatusMessageBuilder
{
    public static bool IsStatusCommand(string text) => text.Trim().ToLowerInvariant() is "/status" or "status" or "what's happening" or "whats happening";
    public static string Build(StatusSnapshot s, DateTimeOffset now, Func<string, string>? redact = null)
    {
        string Limit(string t, int n = 220) => JobMessageBuilder.Clip((redact ?? Redaction.Clean)(t), n);
        var text = s.Busy ? $"Busy\nJob: {Limit(s.Name)}\nProject: {Limit(System.IO.Path.GetFileName(s.Project.TrimEnd('\\', '/')))}\nStarted: {s.Started:O}\nElapsed: {(s.Started is { } at ? now - at : TimeSpan.Zero):hh\\:mm\\:ss}" : "Idle";
        text += $"\nAgents: {string.Join("; ", (s.Agents ?? []).Take(10).Select(a => Limit(a, 100)))}\nTask: {Limit(s.Task)}\nStep: {Limit(s.Step)}\nProgress: {s.Done}/{s.Total}\nLoop: {Limit(s.Loop)}";
        text += "\nOutput:\n" + string.Join('\n', (s.Output ?? []).TakeLast(5).Select(x => Limit(x, 180)));
        text += "\nRecent errors:\n" + string.Join('\n', (s.Errors ?? []).TakeLast(3).Select(x => Limit(x, 180)));
        return JobMessageBuilder.Clip((redact ?? Redaction.Clean)(text), 3500);
    }

    public static StatusSnapshot FromFiles(string directory)
    {
        var snapshot = new StatusSnapshot();
        try
        {
            var file = System.IO.Path.Combine(directory, "supervisor-state.json");
            var tasks = File.Exists(file) ? JsonSerializer.Deserialize<List<SupervisorTask>>(File.ReadAllText(file), new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? [] : [];
            var active = tasks.LastOrDefault(t => t.Status is QueueTaskState.Running or QueueTaskState.WaitingReset);
            snapshot = snapshot with { Busy = active?.Status == QueueTaskState.Running, Name = active?.Goal ?? "", Project = active?.Project ?? "", Task = active?.Goal ?? "", Step = active?.Step.ToString() ?? "", Done = tasks.Count(t => t.Status == QueueTaskState.Completed), Total = tasks.Count,
                Loop = active == null ? "Idle" : $"{active.Status}, round {active.Runs}, next run: {active.ResetAt?.ToString("O") ?? "unscheduled"}" };
            var runs = System.IO.Path.Combine(directory, "runs");
            var newest = Directory.Exists(runs) ? Directory.GetFiles(runs, "*.jsonl").OrderByDescending(File.GetLastWriteTimeUtc).FirstOrDefault() : null;
            if (newest != null)
            {
                var entries = RunLog.Read(newest);
                var request = entries.FirstOrDefault(e => e.Kind == "request");
                if (active == null && request != null) snapshot = snapshot with {
                    Busy = entries.LastOrDefault()?.Kind != "complete", Name = request.Title.Length > 0 ? request.Title : request.Text,
                    Project = request.Project, Task = entries.LastOrDefault(e => e.Title.Length > 0)?.Title ?? "",
                    Step = entries.LastOrDefault()?.Kind ?? ""
                };
                snapshot = snapshot with { Started = DateTimeOffset.TryParse(entries.FirstOrDefault()?.At, out var at) ? at : null,
                    Output = entries.Where(e => e.Text.Length > 0).SelectMany(e => e.Text.Replace("\r", "").Split('\n')).Where(t => t.Length > 0).TakeLast(5).ToArray(),
                    Errors = entries.Where(e => e.Kind.Contains("error", StringComparison.OrdinalIgnoreCase)).TakeLast(3).Select(e => e.Text).ToArray(),
                    Agents = entries.Where(e => e.Role.Length > 0).GroupBy(e => (e.Role, e.Tier)).Select(g => $"{g.Key.Role} {g.Key.Tier}: {g.Last().Kind}").ToArray() };
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException) { }
        return snapshot;
    }
}
