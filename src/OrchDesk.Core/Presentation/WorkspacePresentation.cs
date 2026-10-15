using System.Text.Json;

namespace OrchDesk.Core.Presentation;

public sealed record PaletteCommand(string Id, string Title, string Category, string[] Arguments)
{
    public static IReadOnlyList<PaletteCommand> Search(IEnumerable<PaletteCommand> commands, string query)
    {
        var words = query.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return commands.Where(c => words.All(w => $"{c.Title} {c.Category} {string.Join(' ', c.Arguments)}".Contains(w, StringComparison.OrdinalIgnoreCase)))
            .OrderByDescending(c => c.Title.StartsWith(query.Trim(), StringComparison.OrdinalIgnoreCase)).ThenBy(c => c.Category).ThenBy(c => c.Title).ToArray();
    }
}

public sealed record FileNode(string Name, string RelativePath, string? Change, IReadOnlyList<FileNode> Children)
{
    public static IReadOnlyList<FileNode> Build(IEnumerable<string> paths, IReadOnlyDictionary<string, string>? changes = null)
    {
        var safe = paths.Where(p => !Path.IsPathRooted(p) && !p.Contains(':')).Select(p => p.Replace('\\', '/').Trim('/')).Where(p => p.Length > 0 && !p.Split('/').Any(s => s is ".." or "." || s.Contains("CRM", StringComparison.OrdinalIgnoreCase))).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        IReadOnlyList<FileNode> Level(string prefix) => safe.Where(p => p.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)).Select(p => p[prefix.Length..].Split('/')[0]).Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(name => { var path = prefix + name; var children = Level(path + "/"); return new FileNode(name, path, changes?.GetValueOrDefault(path), children); })
            .OrderBy(n => n.Children.Count == 0).ThenBy(n => n.Name, StringComparer.OrdinalIgnoreCase).ToArray();
        return Level("");
    }
    public static IReadOnlyList<FileNode> Read(string root)
    {
        var paths = new List<string>();
        void Walk(string folder, int depth)
        {
            if (depth > 5 || paths.Count >= 1500) return;
            try { foreach (var entry in Directory.EnumerateFileSystemEntries(folder).Order()) {
                var name = Path.GetFileName(entry);
                if (name.Contains("CRM", StringComparison.OrdinalIgnoreCase) || name is ".git" or ".github" or "bin" or "obj" or "node_modules" || (File.GetAttributes(entry) & FileAttributes.ReparsePoint) != 0) continue;
                paths.Add(Path.GetRelativePath(root, entry));
                if (Directory.Exists(entry)) Walk(entry, depth + 1);
                if (paths.Count >= 1500) break;
            }} catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        }
        if (Directory.Exists(root)) Walk(root, 0);
        return Build(paths);
    }
}

public sealed record ProviderUsage(string ProviderId, int Messages, int Runs, int Steps);
public sealed record RunBudget(int Steps, int MaxSteps, int Round, int MaxRounds, string Status)
{
    public static RunBudget? ReadLatest(string root)
    {
        var directory = Path.Combine(root, "runs"); if (!Directory.Exists(directory)) return null;
        try {
            foreach (var file in Directory.EnumerateFiles(directory, "*.json", SearchOption.AllDirectories).Where(p => !p.Contains("CRM", StringComparison.OrdinalIgnoreCase) && (Path.GetDirectoryName(p) == directory || Path.GetFileName(p) == "checkpoint.json")).OrderByDescending(File.GetLastWriteTimeUtc)) {
                try { var run = JsonSerializer.Deserialize<Overnight.OvernightRun>(File.ReadAllText(file), new JsonSerializerOptions { PropertyNameCaseInsensitive = true }); if (run is not null) return new(run.Steps, run.MaxSteps, run.Round, run.MaxRounds, run.Status); }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException) { }
            }
        } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        return null;
    }
}
public static class UsageAggregator
{
    /// <summary>Observed local activity, never an estimate of a subscription's remaining credit.</summary>
    public static IReadOnlyList<ProviderUsage> Read(string root)
    {
        var totals = new Dictionary<string, (int messages, int runs, int steps)>(StringComparer.OrdinalIgnoreCase);
        void Add(string id, int messages, int runs, int steps) { var old = totals.GetValueOrDefault(id); totals[id] = (old.messages + messages, old.runs + runs, old.steps + steps); }
        foreach (var folder in new[] { "history", "conversations", "runs" }) {
            var directory = Path.Combine(root, folder); if (!Directory.Exists(directory)) continue;
            foreach (var file in Directory.EnumerateFiles(directory, "*.json", SearchOption.AllDirectories).Where(p => !p.Contains("CRM", StringComparison.OrdinalIgnoreCase))) {
                try {
                    using var doc = JsonDocument.Parse(File.ReadAllText(file)); var data = doc.RootElement;
                    JsonElement Get(string name) => data.ValueKind == JsonValueKind.Object ? data.EnumerateObject().FirstOrDefault(p => p.Name.Equals(name, StringComparison.OrdinalIgnoreCase)).Value : default;
                    string? Str(string name) => Get(name).ValueKind == JsonValueKind.String ? Get(name).GetString() : null;
                    var id = Str("providerId") ?? Str("managerId") ?? Str("model") ?? "Unattributed";
                    var messages = Get("messages"); var count = messages.ValueKind == JsonValueKind.Array ? messages.GetArrayLength() : 0;
                    if (folder == "runs") {
                        if (Path.GetDirectoryName(file) != directory && Path.GetFileName(file) != "checkpoint.json") continue;
                        Add(id, count, 1, Get("steps").TryGetInt32Safe());
                    } else Add(id, count, 0, 0);
                } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException) { }
            }
        }
        return totals.OrderBy(p => p.Key).Select(p => new ProviderUsage(p.Key, p.Value.messages, p.Value.runs, p.Value.steps)).ToArray();
    }
    private static int TryGetInt32Safe(this JsonElement value) => value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var number) ? Math.Max(0, number) : 0;
}
