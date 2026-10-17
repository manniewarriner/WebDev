using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace OrchDesk.Core.Automation;

// Unattended runs can never be more permissive than these two modes: Plan (read-only, the default) or Auto (routine in-project work; risky actions are denied, not asked).
[JsonConverter(typeof(JsonStringEnumConverter))] public enum ScheduleMode { Plan, Auto }

public sealed class ScheduledJob
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N")[..8];
    public string Name { get; set; } = "";
    public string Prompt { get; set; } = "";
    public string Project { get; set; } = "";
    public string Time { get; set; } = "09:00";          // local HH:mm
    public int Days { get; set; } = 127;                  // bit 0 = Sunday ... bit 6 = Saturday
    public ScheduleMode Mode { get; set; } = ScheduleMode.Plan;
    public bool Enabled { get; set; } = true;
    public string LastRun { get; set; } = "";             // yyyy-MM-dd of the last start
    public string Created { get; set; } = DateTime.Now.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture); // renewed when you resume it
    public int Failures { get; set; }                     // consecutive failed runs; the job pauses itself at MaxFailures
    public int MaxMinutes { get; set; } = 30;             // wall-clock limit for one run
    public override string ToString() => $"{Name} · {Time} · {(Days == 127 ? "daily" : "some days")} · {Mode}" + (Enabled ? "" : " · paused" + (Failures >= ScheduleStore.MaxFailures ? " after repeated failures" : "")) + (ScheduleStore.Expired(this, DateTime.Now) ? " · expired, resume to renew" : "");
}

public sealed class ScheduleStore(string? path = null)
{
    public const int MaxJobs = 20, MaxPromptChars = 4000, CatchUpMinutes = 60, MaxFailures = 3, ExpireDays = 30;
    private readonly object gate = new();
    private readonly List<ScheduledJob> jobs = Load(path);

    public IReadOnlyList<ScheduledJob> Jobs { get { lock (gate) return jobs.ToList(); } }

    public static void Validate(ScheduledJob job)
    {
        if (string.IsNullOrWhiteSpace(job.Name) || job.Name.Length > 60) throw new ArgumentException("Give the schedule a name of up to 60 characters.");
        if (string.IsNullOrWhiteSpace(job.Prompt) || job.Prompt.Length > MaxPromptChars) throw new ArgumentException($"The task must be 1 to {MaxPromptChars} characters.");
        if (!TimeOnly.TryParseExact(job.Time, "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out _)) throw new ArgumentException("Time must look like 07:30.");
        if (job.Days is < 1 or > 127) throw new ArgumentException("Pick at least one day.");
        if (string.IsNullOrWhiteSpace(job.Project) || !Directory.Exists(job.Project)) throw new ArgumentException("Pick an existing project folder.");
    }

    public void Add(ScheduledJob job)
    {
        Validate(job);
        lock (gate) { if (jobs.Count >= MaxJobs) throw new InvalidOperationException($"At most {MaxJobs} schedules."); jobs.Add(job); Save(); }
    }
    public bool Remove(string id) { lock (gate) { int n = jobs.RemoveAll(j => j.Id == id); if (n > 0) Save(); return n > 0; } }
    // Resuming a job renews it for another ExpireDays and clears its failure count.
    public void SetEnabled(string id, bool enabled) { lock (gate) { if (jobs.FirstOrDefault(j => j.Id == id) is { } j) { j.Enabled = enabled; if (enabled) { j.Failures = 0; j.Created = DateTime.Now.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture); } Save(); } } }
    public void RecordResult(string id, bool ok) { lock (gate) { if (jobs.FirstOrDefault(j => j.Id == id) is { } j) { j.Failures = ok ? 0 : j.Failures + 1; if (j.Failures >= MaxFailures) j.Enabled = false; Save(); } } }
    public static bool Expired(ScheduledJob j, DateTime now) => DateTime.TryParseExact(j.Created, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var c) && now.Date > c.AddDays(ExpireDays);
    public void MarkRan(string id, DateTime now) { lock (gate) { if (jobs.FirstOrDefault(j => j.Id == id) is { } j) { j.LastRun = now.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture); Save(); } } }

    // A job is due from its time until CatchUpMinutes later, once per day. Older misses are skipped rather than run late and unexpectedly.
    public IReadOnlyList<ScheduledJob> Due(DateTime now)
    {
        var today = now.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        lock (gate)
            return jobs.Where(j => j.Enabled && !Expired(j, now) && j.LastRun != today && (j.Days & (1 << (int)now.DayOfWeek)) != 0 && TimeOnly.TryParseExact(j.Time, "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out var t)
                && now.TimeOfDay >= t.ToTimeSpan() && now.TimeOfDay < t.ToTimeSpan() + TimeSpan.FromMinutes(CatchUpMinutes)).ToList();
    }

    private static List<ScheduledJob> Load(string? path)
    {
        try { if (path != null && File.Exists(path)) return (JsonSerializer.Deserialize<List<ScheduledJob>>(File.ReadAllText(path)) ?? []).Where(j => { try { Validate(j); return true; } catch (ArgumentException) { return false; } }).Take(MaxJobs).ToList(); }
        catch (Exception e) when (e is IOException or JsonException) { /* start empty */ }
        return [];
    }
    private void Save()
    {
        if (path == null) return;
        try { Directory.CreateDirectory(Path.GetDirectoryName(path)!); File.WriteAllText(path + ".tmp", JsonSerializer.Serialize(jobs)); File.Move(path + ".tmp", path, true); }
        catch (IOException) { /* keep running */ }
    }
}
