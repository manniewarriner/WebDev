using System.Text;
using System.Text.RegularExpressions;

namespace OrchDesk.Core.Orchestration;

// A token-budgeted map of the project: the files that matter most and the symbols they define. Files are ranked with PageRank over a
// "file A uses a symbol defined in file B" graph, the way Aider builds its repo map, so workers get orientation without the whole repo.
public sealed class RepoMap
{
    public sealed record Entry(string Path, IReadOnlyList<string> Symbols);

    private static readonly HashSet<string> SkipDirs = new(StringComparer.OrdinalIgnoreCase) { "bin", "obj", "node_modules", "packages", "dist", "build", "out", "target", "vendor", "__pycache__", "publish", "assets", "attachments" };
    private static readonly Dictionary<string, Regex[]> Patterns = new(StringComparer.OrdinalIgnoreCase)
    {
        [".cs"] = [new(@"\b(?:class|interface|struct|enum|record)\s+([A-Za-z_]\w*)", RegexOptions.Compiled), new(@"^\s*(?:public|internal|protected|private)[\w\s<>\[\],\.\?]*?\s([A-Za-z_]\w*)\s*\(", RegexOptions.Compiled | RegexOptions.Multiline)],
        [".java"] = [new(@"\b(?:class|interface|enum|record)\s+([A-Za-z_]\w*)", RegexOptions.Compiled), new(@"^\s*(?:public|protected|private)[\w\s<>\[\],\.\?]*?\s([A-Za-z_]\w*)\s*\(", RegexOptions.Compiled | RegexOptions.Multiline)],
        [".kt"] = [new(@"\b(?:class|interface|object|fun)\s+([A-Za-z_]\w*)", RegexOptions.Compiled)],
        [".ts"] = [new(@"\b(?:function|class|interface|type|enum)\s+([A-Za-z_]\w*)", RegexOptions.Compiled), new(@"\bexport\s+(?:const|let)\s+([A-Za-z_]\w*)", RegexOptions.Compiled)],
        [".tsx"] = [new(@"\b(?:function|class|interface|type|enum)\s+([A-Za-z_]\w*)", RegexOptions.Compiled), new(@"\bexport\s+(?:const|let)\s+([A-Za-z_]\w*)", RegexOptions.Compiled)],
        [".js"] = [new(@"\b(?:function|class)\s+([A-Za-z_]\w*)", RegexOptions.Compiled), new(@"\bexport\s+(?:const|let)\s+([A-Za-z_]\w*)", RegexOptions.Compiled)],
        [".py"] = [new(@"^\s*(?:def|class)\s+([A-Za-z_]\w*)", RegexOptions.Compiled | RegexOptions.Multiline)],
        [".go"] = [new(@"\bfunc\s+(?:\([^)]*\)\s*)?([A-Za-z_]\w*)", RegexOptions.Compiled), new(@"\btype\s+([A-Za-z_]\w*)\s+(?:struct|interface)", RegexOptions.Compiled)],
        [".rs"] = [new(@"\b(?:fn|struct|enum|trait)\s+([A-Za-z_]\w*)", RegexOptions.Compiled)],
        [".lua"] = [new(@"\bfunction\s+(?:[\w\.]+[\.:])?([A-Za-z_]\w*)\s*\(", RegexOptions.Compiled)],
        [".luau"] = [new(@"\bfunction\s+(?:[\w\.]+[\.:])?([A-Za-z_]\w*)\s*\(", RegexOptions.Compiled), new(@"\btype\s+([A-Za-z_]\w*)\s*=", RegexOptions.Compiled)],
    };
    private static readonly Regex Word = new(@"[A-Za-z_]\w{2,}", RegexOptions.Compiled);

    private readonly List<Entry> entries = [];
    private readonly Dictionary<string, double> rank = new(StringComparer.OrdinalIgnoreCase);
    public IReadOnlyList<Entry> Files => entries;
    public int FileCount => entries.Count;
    public double RankOf(string path) => rank.GetValueOrDefault(path);

    public static RepoMap Build(string root, int maxFiles = 2500, int maxFileBytes = 200_000)
    {
        var map = new RepoMap(); var defs = new Dictionary<string, List<int>>(); var words = new List<Dictionary<string, int>>();
        foreach (var file in Walk(root, maxFiles))
        {
            string text; try { var info = new FileInfo(file); if (info.Length > maxFileBytes) continue; text = File.ReadAllText(file); } catch (Exception e) when (e is IOException or UnauthorizedAccessException) { continue; }
            var symbols = new List<string>();
            foreach (var re in Patterns[Path.GetExtension(file)])
                foreach (Match m in re.Matches(text)) { var s = m.Groups[1].Value; if (s.Length >= 3 && !symbols.Contains(s)) symbols.Add(s); }
            int index = map.entries.Count;
            map.entries.Add(new(Path.GetRelativePath(root, file).Replace('\\', '/'), symbols));
            foreach (var s in symbols) { if (!defs.TryGetValue(s, out var list)) defs[s] = list = []; list.Add(index); }
            var counts = new Dictionary<string, int>(); foreach (Match w in Word.Matches(text)) counts[w.Value] = counts.GetValueOrDefault(w.Value) + 1; words.Add(counts);
        }
        map.Rank(defs, words);
        return map;
    }

    private static IEnumerable<string> Walk(string root, int maxFiles)
    {
        int seen = 0; var stack = new Stack<string>(); stack.Push(root);
        while (stack.Count > 0 && seen < maxFiles)
        {
            var dir = stack.Pop(); IEnumerable<string> files, dirs;
            try { files = Directory.EnumerateFiles(dir).ToList(); dirs = Directory.EnumerateDirectories(dir).ToList(); } catch (Exception e) when (e is IOException or UnauthorizedAccessException) { continue; }
            foreach (var file in files.OrderBy(f => f, StringComparer.OrdinalIgnoreCase))
            {
                if (!Patterns.ContainsKey(Path.GetExtension(file))) continue;
                if ((File.GetAttributes(file) & FileAttributes.ReparsePoint) != 0) continue;
                if (++seen > maxFiles) yield break; yield return file;
            }
            foreach (var d in dirs.OrderByDescending(d => d, StringComparer.OrdinalIgnoreCase))
            {
                var name = Path.GetFileName(d);
                if (name.StartsWith('.') || SkipDirs.Contains(name) || (File.GetAttributes(d) & FileAttributes.ReparsePoint) != 0) continue;
                stack.Push(d);
            }
        }
    }

    private void Rank(Dictionary<string, List<int>> defs, List<Dictionary<string, int>> words)
    {
        int n = entries.Count; if (n == 0) return;
        // Edge from a file that uses a symbol to the file that defines it. Symbols defined in many files (Main, Run, ToString...) say nothing.
        var edges = new Dictionary<(int From, int To), double>();
        for (int from = 0; from < n; from++)
            foreach (var (word, count) in words[from])
                if (defs.TryGetValue(word, out var owners) && owners.Count <= 5)
                    foreach (var to in owners) if (to != from) edges[(from, to)] = edges.GetValueOrDefault((from, to)) + Math.Sqrt(count);
        var outWeight = new double[n]; foreach (var ((from, _), w) in edges) outWeight[from] += w;
        var score = Enumerable.Repeat(1.0 / n, n).ToArray();
        for (int iteration = 0; iteration < 25; iteration++)
        {
            var next = Enumerable.Repeat(0.15 / n, n).ToArray(); double dangling = 0;
            for (int i = 0; i < n; i++) if (outWeight[i] == 0) dangling += score[i];
            foreach (var ((from, to), w) in edges) next[to] += 0.85 * score[from] * w / outWeight[from];
            for (int i = 0; i < n; i++) next[i] += 0.85 * dangling / n;
            score = next;
        }
        for (int i = 0; i < n; i++) rank[entries[i].Path] = score[i];
    }

    // Most important files first, as many as fit the character budget (about four characters per token).
    public string Render(int budgetChars, IEnumerable<string>? pin = null)
    {
        if (entries.Count == 0) return "";
        var pinned = new HashSet<string>(pin ?? [], StringComparer.OrdinalIgnoreCase);
        var sb = new StringBuilder("Repository map (most important files first; symbols each file defines):\n");
        foreach (var e in entries.OrderByDescending(e => pinned.Contains(e.Path)).ThenByDescending(e => RankOf(e.Path)).ThenBy(e => e.Path, StringComparer.OrdinalIgnoreCase))
        {
            var line = e.Path + (e.Symbols.Count > 0 ? ": " + string.Join(", ", e.Symbols.Take(10)) : "") + "\n";
            if (sb.Length + line.Length > budgetChars) { if (sb.Length < 120) sb.Append(line[..Math.Min(line.Length, Math.Max(0, budgetChars - sb.Length))]); break; }
            sb.Append(line);
        }
        return sb.ToString().TrimEnd();
    }

    // Files a piece of text is about: a file name or a defined symbol mentioned in it.
    public IReadOnlyList<string> Focus(string text, int max = 8)
    {
        if (string.IsNullOrWhiteSpace(text)) return [];
        var mentioned = new HashSet<string>(Word.Matches(text).Select(m => m.Value), StringComparer.OrdinalIgnoreCase);
        return entries.Select(e => (e.Path, Score: (mentioned.Contains(System.IO.Path.GetFileNameWithoutExtension(e.Path)) ? 3 : 0) + (text.Contains(e.Path, StringComparison.OrdinalIgnoreCase) ? 3 : 0) + e.Symbols.Count(mentioned.Contains)))
            .Where(x => x.Score > 0).OrderByDescending(x => x.Score).ThenByDescending(x => RankOf(x.Path)).Take(max).Select(x => x.Path).ToList();
    }

    public string RenderFocus(IReadOnlyList<string> files, int perFile = 12)
        => files.Count == 0 ? "" : "Files most relevant to this task:\n" + string.Join("\n", files.Select(f => entries.First(e => e.Path.Equals(f, StringComparison.OrdinalIgnoreCase)) is var e ? $"- {e.Path}" + (e.Symbols.Count > 0 ? ": " + string.Join(", ", e.Symbols.Take(perFile)) : "") : f));
}
