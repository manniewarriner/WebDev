using System.Text.Json;

namespace OrchDesk.Core.Overnight;

public enum OvernightStatus { Queued, Running, Succeeded, Failed, NeedsAttention, Cancelled }
public enum OvernightMode { Auto, Supervisor }

public sealed class OvernightTask
{
    public string Id { get; set; } = DateTime.UtcNow.ToString("yyyyMMddHHmmss") + "-" + Guid.NewGuid().ToString("N")[..6];
    public string Title { get; set; } = "";
    public string Prompt { get; set; } = "";
    public string Workspace { get; set; } = "";
    public OvernightMode Mode { get; set; } = OvernightMode.Auto;
    public string? PreferredProviderId { get; set; }
    public string? TestCommand { get; set; }
    public OvernightStatus Status { get; set; } = OvernightStatus.Queued;
    public int Attempts { get; set; }
    public string? Branch { get; set; }
    public string? WorktreePath { get; set; }
    public DateTimeOffset? StartedAt { get; set; }
    public DateTimeOffset? FinishedAt { get; set; }
    public string? ProviderUsed { get; set; }
    public bool? TestsPassed { get; set; }
    public string? DiffStat { get; set; }
    public string? Summary { get; set; }
    public string? Error { get; set; }
    public string? LogPath { get; set; }
    public bool Pushed { get; set; }
    public List<string> Notes { get; set; } = [];
}

/// <summary>
/// The queue lives in a JSON file so it survives crashes and reboots. A task found "Running" on load was
/// interrupted (crash, Windows Update reboot), so it goes back to Queued with a note.
/// </summary>
public sealed class OvernightQueueStore(string directory)
{
    private readonly object _lock = new();
    public string FilePath => Path.Combine(directory, "queue.json");
    public string Directory => directory;

    public List<OvernightTask> Load()
    {
        lock (_lock)
        {
            if (!File.Exists(FilePath)) return [];
            var tasks = JsonSerializer.Deserialize<List<OvernightTask>>(File.ReadAllText(FilePath), Config.ConfigStore.Json) ?? [];
            foreach (var t in tasks.Where(t => t.Status == OvernightStatus.Running))
            {
                t.Status = OvernightStatus.Queued;
                t.Notes.Add($"{DateTimeOffset.Now:g}: was interrupted while running; re-queued.");
            }
            return tasks;
        }
    }

    public void Save(IEnumerable<OvernightTask> tasks)
    {
        lock (_lock)
        {
            System.IO.Directory.CreateDirectory(directory);
            var tmp = FilePath + ".tmp";
            File.WriteAllText(tmp, JsonSerializer.Serialize(tasks.ToList(), Config.ConfigStore.Json));
            File.Move(tmp, FilePath, overwrite: true);
        }
    }
}
