using System.Globalization;
using System.Text.Json;

using OrchDesk.Core.Safety;

namespace OrchDesk.Core.Dashboard;

public sealed class TaskItem
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N")[..10];
    public string Title { get; set; } = "";
    public string Project { get; set; } = "";
    public string Priority { get; set; } = "P2";
    public string? Due { get; set; }          // yyyy-MM-dd, London calendar date
    public string Status { get; set; } = "todo";
    public string Notes { get; set; } = "";
    public string Source { get; set; } = "app"; // app, api, or the bot/source that created it
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? CompletedAt { get; set; }
}

// Only the fields that are set are changed. ClearDue removes the due date.
public sealed record TaskPatch(string? Title = null, string? Project = null, string? Priority = null, string? Due = null, bool ClearDue = false, string? Status = null, string? Notes = null);

public sealed record TodayView(IReadOnlyList<TaskItem> Overdue, IReadOnlyList<TaskItem> DueToday, IReadOnlyList<TaskItem> InProgress, string Date);

public static class TaskRules
{
    public static readonly string[] Priorities = ["P1", "P2", "P3"];
    public static readonly string[] Statuses = ["todo", "in_progress", "done"];
    public const int MaxTitle = 200, MaxProject = 60, MaxNotes = 4000, MaxSource = 40;

    // The dashboard's "today" is the London calendar day, wherever the computer's clock is set.
    public static TimeZoneInfo London()
    {
        foreach (var id in new[] { "Europe/London", "GMT Standard Time" })
            try { return TimeZoneInfo.FindSystemTimeZoneById(id); }
            catch (Exception e) when (e is TimeZoneNotFoundException or InvalidTimeZoneException) { }
        return TimeZoneInfo.Local;
    }
    public static string LondonDate(DateTime utcNow) => TimeZoneInfo.ConvertTimeFromUtc(utcNow, London()).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    public static string? NormalizeDue(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        if (!DateOnly.TryParseExact(value.Trim(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d)) throw new ArgumentException("Due date must look like 2026-10-31.");
        return d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
    }
    public static string NormalizePriority(string? value)
    {
        var p = string.IsNullOrWhiteSpace(value) ? "P2" : value.Trim().ToUpperInvariant();
        if (!Priorities.Contains(p)) throw new ArgumentException("Priority must be P1, P2 or P3.");
        return p;
    }
    public static string NormalizeStatus(string? value)
    {
        var s = string.IsNullOrWhiteSpace(value) ? "todo" : value.Trim().ToLowerInvariant().Replace('-', '_').Replace(' ', '_');
        if (!Statuses.Contains(s)) throw new ArgumentException("Status must be todo, in_progress or done.");
        return s;
    }
    // Text from the app, the local API or a bot: invisible characters removed, length capped.
    public static string Text(string? value, int max, string field, bool required = false)
    {
        var t = TextSafety.Strip(value ?? "").Trim();
        if (required && t.Length == 0) throw new ArgumentException($"{field} is required.");
        if (t.Length > max) throw new ArgumentException($"{field} is at most {max} characters.");
        return t;
    }

    public static int PriorityRank(TaskItem t) => Array.IndexOf(Priorities, t.Priority) is var i and >= 0 ? i : 1;

    // Overdue and due-today tasks that are still open. In progress shows every open task marked in progress, due or not.
    public static TodayView Today(IEnumerable<TaskItem> tasks, string today)
    {
        var open = tasks.Where(t => t.Status != "done").ToList();
        return new(
            open.Where(t => t.Due != null && string.CompareOrdinal(t.Due, today) < 0).OrderBy(t => t.Due, StringComparer.Ordinal).ThenBy(PriorityRank).ToList(),
            open.Where(t => t.Due == today).OrderBy(PriorityRank).ThenBy(t => t.CreatedAt).ToList(),
            open.Where(t => t.Status == "in_progress").OrderBy(PriorityRank).ThenBy(t => t.Due ?? "9999", StringComparer.Ordinal).ToList(),
            today);
    }
}

public sealed class TaskStore(string? path = null)
{
    public const int MaxTasks = 5000;
    private readonly object gate = new();
    private readonly List<TaskItem> items = Load(path);

    public int Count { get { lock (gate) return items.Count; } }

    public IReadOnlyList<TaskItem> List(string? project = null, string? status = null)
    {
        lock (gate)
            return items
                .Where(t => string.IsNullOrEmpty(project) || string.Equals(t.Project, project, StringComparison.OrdinalIgnoreCase))
                .Where(t => status == null || t.Status == status)
                .OrderBy(TaskRules.PriorityRank).ThenBy(t => t.Due ?? "9999", StringComparer.Ordinal).ThenBy(t => t.CreatedAt).ToList();
    }

    public TaskItem? Get(string id) { lock (gate) return items.FirstOrDefault(t => t.Id == id); }

    public TaskItem Add(string title, string project = "", string priority = "P2", string? due = null, string notes = "", string source = "app")
    {
        var item = new TaskItem
        {
            Title = TaskRules.Text(title, TaskRules.MaxTitle, "Title", required: true),
            Project = TaskRules.Text(project, TaskRules.MaxProject, "Project"),
            Priority = TaskRules.NormalizePriority(priority),
            Due = TaskRules.NormalizeDue(due),
            Notes = TaskRules.Text(notes, TaskRules.MaxNotes, "Notes"),
            Source = TaskRules.Text(source, TaskRules.MaxSource, "Source") is { Length: > 0 } s ? s : "app",
        };
        lock (gate)
        {
            if (items.Count >= MaxTasks) throw new InvalidOperationException($"The task list is full ({MaxTasks} tasks). Delete finished tasks first.");
            items.Add(item); Save();
        }
        return item;
    }

    public TaskItem Update(string id, TaskPatch patch)
    {
        lock (gate)
        {
            var t = items.FirstOrDefault(x => x.Id == id) ?? throw new KeyNotFoundException("No task with that id.");
            // Validate everything before changing anything, so a bad field never leaves a half-updated task.
            var title = patch.Title == null ? t.Title : TaskRules.Text(patch.Title, TaskRules.MaxTitle, "Title", required: true);
            var project = patch.Project == null ? t.Project : TaskRules.Text(patch.Project, TaskRules.MaxProject, "Project");
            var priority = patch.Priority == null ? t.Priority : TaskRules.NormalizePriority(patch.Priority);
            var due = patch.ClearDue ? null : patch.Due == null ? t.Due : TaskRules.NormalizeDue(patch.Due);
            var notes = patch.Notes == null ? t.Notes : TaskRules.Text(patch.Notes, TaskRules.MaxNotes, "Notes");
            var status = patch.Status == null ? t.Status : TaskRules.NormalizeStatus(patch.Status);

            t.Title = title; t.Project = project; t.Priority = priority; t.Due = due; t.Notes = notes;
            if (status != t.Status)
            {
                t.CompletedAt = status == "done" ? DateTime.UtcNow : null;
                t.Status = status;
            }
            t.UpdatedAt = DateTime.UtcNow;
            Save();
            return t;
        }
    }

    public bool Delete(string id) { lock (gate) { int n = items.RemoveAll(t => t.Id == id); if (n > 0) Save(); return n > 0; } }

    private static List<TaskItem> Load(string? path)
    {
        try { if (path != null && File.Exists(path)) return JsonSerializer.Deserialize<List<TaskItem>>(File.ReadAllText(path)) ?? []; }
        catch (Exception e) when (e is IOException or JsonException) { /* start empty; the file is kept for inspection */ }
        return [];
    }
    private void Save()
    {
        if (path == null) return;
        try { Directory.CreateDirectory(Path.GetDirectoryName(path)!); File.WriteAllText(path + ".tmp", JsonSerializer.Serialize(items)); File.Move(path + ".tmp", path, true); }
        catch (IOException) { /* a failed save must not break the app */ }
    }
}

public sealed class ReportItem
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N")[..10];
    public string Source { get; set; } = "";     // e.g. Jarvis, Jordan, Stark
    public string Type { get; set; } = "update"; // e.g. brief, monitor, weekly, alert, update
    public string Title { get; set; } = "";
    public string Body { get; set; } = "";
    public string? DataJson { get; set; }        // optional JSON object with structured fields
    public string Date { get; set; } = "";       // London calendar date the report is about
    public DateTime ReceivedAt { get; set; } = DateTime.UtcNow;
}

public sealed class ReportStore(string? path = null)
{
    public const int MaxReports = 1000, MaxBody = 20000, MaxData = 20000;
    private static readonly System.Text.RegularExpressions.Regex LabelPattern = new(@"^[\p{L}\p{N} ._-]{1,40}$", System.Text.RegularExpressions.RegexOptions.Compiled);
    private readonly object gate = new();
    private readonly List<ReportItem> items = Load(path);

    public IReadOnlyList<ReportItem> List(string? source = null, string? type = null, int limit = 50)
    {
        lock (gate)
            return items
                .Where(r => string.IsNullOrEmpty(source) || string.Equals(r.Source, source, StringComparison.OrdinalIgnoreCase))
                .Where(r => string.IsNullOrEmpty(type) || string.Equals(r.Type, type, StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(r => r.ReceivedAt).Take(Math.Clamp(limit, 1, 200)).ToList();
    }

    public ReportItem? Get(string id) { lock (gate) return items.FirstOrDefault(r => r.Id == id); }

    public ReportItem Add(string source, string type, string title, string body, string? dataJson, string? date, DateTime utcNow)
    {
        source = CleanLabel(source, "Source"); type = string.IsNullOrWhiteSpace(type) ? "update" : CleanLabel(type, "Type");
        var report = new ReportItem
        {
            Source = source, Type = type,
            Title = TaskRules.Text(title, TaskRules.MaxTitle, "Title", required: true),
            Body = TaskRules.Text(body, MaxBody, "Body"),
            DataJson = DataObject(dataJson),
            Date = string.IsNullOrWhiteSpace(date) ? TaskRules.LondonDate(utcNow) : TaskRules.NormalizeDue(date)!,
            ReceivedAt = utcNow,
        };
        if (report.Body.Length == 0 && report.DataJson == null) throw new ArgumentException("A report needs a body or data.");
        lock (gate)
        {
            items.Add(report);
            while (items.Count > MaxReports) items.RemoveAt(0);
            Save();
        }
        return report;
    }

    public bool Delete(string id) { lock (gate) { int n = items.RemoveAll(r => r.Id == id); if (n > 0) Save(); return n > 0; } }

    private static string CleanLabel(string value, string field)
    {
        var v = TextSafety.Strip(value ?? "").Trim();
        if (!LabelPattern.IsMatch(v)) throw new ArgumentException($"{field} can use letters, numbers, spaces, dots, dashes and underscores (1 to 40 characters).");
        return v;
    }

    private static string? DataObject(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        if (json.Length > MaxData) throw new ArgumentException($"Data is at most {MaxData} characters.");
        using var doc = JsonDocument.Parse(json);
        if (doc.RootElement.ValueKind != JsonValueKind.Object) throw new ArgumentException("Data must be a JSON object.");
        return doc.RootElement.GetRawText();
    }

    private static List<ReportItem> Load(string? path)
    {
        try { if (path != null && File.Exists(path)) return JsonSerializer.Deserialize<List<ReportItem>>(File.ReadAllText(path)) ?? []; }
        catch (Exception e) when (e is IOException or JsonException) { /* start empty */ }
        return [];
    }
    private void Save()
    {
        if (path == null) return;
        try { Directory.CreateDirectory(Path.GetDirectoryName(path)!); File.WriteAllText(path + ".tmp", JsonSerializer.Serialize(items)); File.Move(path + ".tmp", path, true); }
        catch (IOException) { /* keep running */ }
    }
}
