using System.Text;

namespace OrchDesk.Core.Tools;
public static class TextEdits
{
    public static string Apply(string original, string search, string replacement)
    {
        if (string.IsNullOrEmpty(search)) throw new ArgumentException("Search cannot be empty.");
        static (string Text, List<int> Offsets) Normalize(string source)
        {
            var text = new StringBuilder(); var offsets = new List<int>();
            for (int i = 0; i < source.Length;)
            {
                if (source[i] is ' ' or '\t') { int end = i; while (end < source.Length && source[end] is ' ' or '\t') end++; if (end == source.Length || source[end] is '\r' or '\n') { i = end; continue; } }
                offsets.Add(i); if (source[i] == '\r') { text.Append('\n'); i += i + 1 < source.Length && source[i + 1] == '\n' ? 2 : 1; } else text.Append(source[i++]);
            }
            offsets.Add(source.Length); return (text.ToString(), offsets);
        }
        var (text, map) = Normalize(original); var needle = Normalize(search).Text;
        int at = text.IndexOf(needle, StringComparison.Ordinal);
        if (needle.Length == 0 || at < 0 || text.IndexOf(needle, at + 1, StringComparison.Ordinal) >= 0) throw new InvalidOperationException(at < 0 ? "Search text not found; read the file and retry." : "Ambiguous search text; include more context.");
        var replace = replacement.Replace("\r\n", "\n").Replace('\r', '\n'); if (original.Contains("\r\n")) replace = replace.Replace("\n", "\r\n");
        var result = original[..map[at]] + replace + original[map[at + needle.Length]..];
        static Dictionary<string, int> Pairs(string s) { var lines = s.Replace("\r\n", "\n").Split('\n'); var pairs = new Dictionary<string, int>(); for (int i = 1; i < lines.Length; i++) if (!string.IsNullOrWhiteSpace(lines[i]) && lines[i] == lines[i - 1]) pairs[lines[i]] = pairs.GetValueOrDefault(lines[i]) + 1; return pairs; }
        var before = Pairs(original); if (Pairs(result).Any(p => p.Value > before.GetValueOrDefault(p.Key))) throw new InvalidOperationException("Adjacent duplicate line rejected; change may already be present.");
        return result;
    }
    public static string Diff(string path, string before, string after)
    {
        var a = before.Replace("\r\n", "\n").Split('\n'); var b = after.Replace("\r\n", "\n").Split('\n');
        int prefix = 0; while (prefix < Math.Min(a.Length, b.Length) && a[prefix] == b[prefix]) prefix++;
        int suffix = 0; while (suffix < Math.Min(a.Length, b.Length) - prefix && a[^(suffix + 1)] == b[^(suffix + 1)]) suffix++;
        if (prefix == a.Length && prefix == b.Length) return "No changes.";
        int start = Math.Max(0, prefix - 3), ae = Math.Min(a.Length, a.Length - suffix + 3), be = Math.Min(b.Length, b.Length - suffix + 3);
        return $"--- a/{path}\n+++ b/{path}\n@@ -{start + 1},{ae - start} +{start + 1},{be - start} @@\n" + string.Join('\n', a[start..prefix].Select(l => " " + l).Concat(a[prefix..(a.Length - suffix)].Select(l => "-" + l)).Concat(b[prefix..(b.Length - suffix)].Select(l => "+" + l)).Concat(b[(b.Length - suffix)..be].Select(l => " " + l)));
    }
}
