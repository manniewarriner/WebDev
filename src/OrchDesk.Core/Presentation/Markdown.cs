using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace OrchDesk.Core.Presentation;

public abstract record MdBlock;
public sealed record Heading(int Level, IReadOnlyList<MdSpan> Spans) : MdBlock;
public sealed record Paragraph(IReadOnlyList<MdSpan> Spans) : MdBlock;
public sealed record CodeBlock(string Language, string Code) : MdBlock;
public sealed record ListBlock(bool Ordered, IReadOnlyList<IReadOnlyList<MdSpan>> Items) : MdBlock;
public sealed record Quote(IReadOnlyList<MdSpan> Spans) : MdBlock;
public sealed record Rule : MdBlock;
public sealed record Table(IReadOnlyList<IReadOnlyList<MdSpan>> Header, IReadOnlyList<IReadOnlyList<IReadOnlyList<MdSpan>>> Rows) : MdBlock;
public record MdSpan(string Text, bool Bold = false, bool Italic = false, bool Code = false, string? Link = null);

/// <summary>
/// Small, dependency-free, streaming-tolerant Markdown parser. Never throws.
/// Unclosed fences become a CodeBlock with the text so far; unclosed emphasis stays literal.
/// </summary>
public static class MarkdownParser
{
    private const int MaxDepth = 6;

    private static readonly Regex HeadingRx = new(@"^ {0,3}(#{1,6})[ \t]+(.*?)(?:[ \t]+#+)?[ \t]*$", RegexOptions.CultureInvariant);
    private static readonly Regex RuleRx = new(@"^ {0,3}([-*_])(?:[ \t]*\1){2,}[ \t]*$", RegexOptions.CultureInvariant);
    private static readonly Regex QuoteRx = new(@"^ {0,3}>[ ]?(.*)$", RegexOptions.CultureInvariant);
    private static readonly Regex ListRx = new(@"^([ \t]*)([-*+]|\d{1,9}[.)])(?:[ \t]+(.*))?$", RegexOptions.CultureInvariant);
    private static readonly Regex DelimRx = new(@"^[ \t]*\|?[ \t]*:?-+:?[ \t]*(?:\|[ \t]*:?-+:?[ \t]*)*\|?[ \t]*$", RegexOptions.CultureInvariant);

    public static IReadOnlyList<MdBlock> Parse(string markdown)
    {
        if (string.IsNullOrEmpty(markdown)) return Array.Empty<MdBlock>();
        try
        {
            return ParseBlocks(markdown);
        }
        catch (Exception)
        {
            return new MdBlock[] { new Paragraph(new[] { new MdSpan(markdown) }) };
        }
    }

    private static List<MdBlock> ParseBlocks(string markdown)
    {
        var blocks = new List<MdBlock>();
        string[] lines = markdown.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
        int n = lines.Length;
        int i = 0;
        while (i < n)
        {
            string line = lines[i];
            if (line.Trim().Length == 0) { i++; continue; }

            if (TryFenceOpen(line, out char fence, out int fenceLen, out string info))
            {
                i++;
                var code = new List<string>();
                while (i < n && !IsFenceClose(lines[i], fence, fenceLen)) { code.Add(lines[i]); i++; }
                if (i < n) i++;
                while (code.Count > 0 && code[^1].Trim().Length == 0) code.RemoveAt(code.Count - 1);
                blocks.Add(new CodeBlock(FirstWord(info), string.Join("\n", code)));
                continue;
            }

            var hm = HeadingRx.Match(line);
            if (hm.Success)
            {
                blocks.Add(new Heading(hm.Groups[1].Length, Inline(hm.Groups[2].Value)));
                i++;
                continue;
            }

            if (IsRule(line)) { blocks.Add(new Rule()); i++; continue; }

            if (QuoteRx.IsMatch(line))
            {
                var qLines = new List<string>();
                while (i < n)
                {
                    var qm = QuoteRx.Match(lines[i]);
                    if (!qm.Success) break;
                    qLines.Add(qm.Groups[1].Value);
                    i++;
                }
                blocks.Add(new Quote(Inline(string.Join("\n", qLines))));
                continue;
            }

            if (ParseListLine(line, out bool ordered, out string firstContent))
            {
                var items = new List<List<string>> { new List<string> { firstContent } };
                i++;
                while (i < n)
                {
                    string cur = lines[i];
                    if (cur.Trim().Length == 0)
                    {
                        // Blank lines only keep the list open when another item of the same kind follows.
                        int k = i;
                        while (k < n && lines[k].Trim().Length == 0) k++;
                        if (k < n && !IsRule(lines[k]) && ParseListLine(lines[k], out bool o2, out _) && o2 == ordered)
                        {
                            i = k;
                            continue;
                        }
                        break;
                    }
                    if (IsRule(cur)) break;
                    if (ParseListLine(cur, out bool o, out string content))
                    {
                        if (o != ordered) break;
                        items.Add(new List<string> { content });
                        i++;
                        continue;
                    }
                    if (StartsBlock(lines, i)) break;
                    // Lazy / indented continuation of the current item.
                    items[^1].Add(cur.Trim());
                    i++;
                }
                blocks.Add(new ListBlock(ordered, items.Select(it => (IReadOnlyList<MdSpan>)Inline(string.Join("\n", it))).ToList()));
                continue;
            }

            if (IsTableStart(lines, i))
            {
                var header = SplitRow(lines[i]).Select(c => Inline(c)).ToList();
                i += 2;
                var rows = new List<IReadOnlyList<IReadOnlyList<MdSpan>>>();
                while (i < n && lines[i].Trim().Length > 0 && lines[i].Contains('|') && !IsRule(lines[i]))
                {
                    rows.Add(SplitRow(lines[i]).Select(c => Inline(c)).ToList());
                    i++;
                }
                blocks.Add(new Table(header, rows));
                continue;
            }

            // Paragraph: the first line is guaranteed not to start another block.
            var para = new List<string>();
            do
            {
                para.Add(lines[i].TrimStart(' ', '\t'));
                i++;
            }
            while (i < n && lines[i].Trim().Length > 0 && !StartsBlock(lines, i));
            blocks.Add(new Paragraph(Inline(string.Join("\n", para))));
        }
        return blocks;
    }

    private static bool StartsBlock(string[] lines, int i)
    {
        string line = lines[i];
        return TryFenceOpen(line, out _, out _, out _)
            || HeadingRx.IsMatch(line)
            || IsRule(line)
            || QuoteRx.IsMatch(line)
            || ListRx.IsMatch(line)
            || IsTableStart(lines, i);
    }

    private static bool IsRule(string line) => RuleRx.IsMatch(line);

    private static bool ParseListLine(string line, out bool ordered, out string content)
    {
        var m = ListRx.Match(line);
        ordered = false;
        content = string.Empty;
        if (!m.Success) return false;
        ordered = char.IsDigit(m.Groups[2].Value[0]);
        content = m.Groups[3].Success ? m.Groups[3].Value.Trim() : string.Empty;
        return true;
    }

    private static bool TryFenceOpen(string line, out char fence, out int len, out string info)
    {
        fence = '\0';
        len = 0;
        info = string.Empty;
        string t = line.TrimStart(' ', '\t');
        if (t.Length < 3 || (t[0] != '`' && t[0] != '~')) return false;
        fence = t[0];
        while (len < t.Length && t[len] == fence) len++;
        if (len < 3) return false;
        info = t[len..].Trim();
        return !(fence == '`' && info.Contains('`'));
    }

    private static bool IsFenceClose(string line, char fence, int len)
    {
        string t = line.Trim(' ', '\t');
        return t.Length >= len && t.All(ch => ch == fence);
    }

    private static string FirstWord(string info)
    {
        var parts = info.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
        return parts.Length > 0 ? parts[0] : string.Empty;
    }

    private static bool IsTableStart(string[] lines, int i) =>
        i + 1 < lines.Length
        && lines[i].Contains('|')
        && lines[i + 1].Contains('|')
        && lines[i + 1].Contains('-')
        && DelimRx.IsMatch(lines[i + 1]);

    private static List<string> SplitRow(string row)
    {
        string s = row.Trim();
        if (s.StartsWith('|')) s = s[1..];
        if (s.EndsWith('|') && !s.EndsWith("\\|")) s = s[..^1];
        var cells = new List<string>();
        var sb = new StringBuilder();
        for (int k = 0; k < s.Length; k++)
        {
            char ch = s[k];
            if (ch == '\\' && k + 1 < s.Length && s[k + 1] == '|')
            {
                sb.Append('|');
                k++;
            }
            else if (ch == '|')
            {
                cells.Add(sb.ToString().Trim());
                sb.Clear();
            }
            else
            {
                sb.Append(ch);
            }
        }
        cells.Add(sb.ToString().Trim());
        return cells;
    }

    // ---- inline ----

    private static List<MdSpan> Inline(string s, bool bold = false, bool italic = false, int depth = 0)
    {
        var result = new List<MdSpan>();
        var plain = new StringBuilder();
        int i = 0;
        while (i < s.Length)
        {
            char c = s[i];

            if (c == '\\' && i + 1 < s.Length && IsAsciiPunct(s[i + 1]))
            {
                plain.Append(s[i + 1]);
                i += 2;
                continue;
            }

            if (c == '`')
            {
                int run = CountRun(s, i, '`');
                int close = FindBacktickRun(s, i + run, run);
                if (close >= 0)
                {
                    Flush(result, plain, bold, italic);
                    AddSpan(result, new MdSpan(StripCodePadding(s.Substring(i + run, close - i - run)), bold, italic, true));
                    i = close + run;
                    continue;
                }
                plain.Append('`', run);
                i += run;
                continue;
            }

            if (c == '[' && depth < MaxDepth)
            {
                int close = s.IndexOf(']', i + 1);
                if (close > i + 1 && close + 1 < s.Length && s[close + 1] == '(')
                {
                    int end = s.IndexOf(')', close + 2);
                    if (end > close + 1)
                    {
                        string url = ExtractUrl(s.Substring(close + 2, end - close - 2));
                        if (url.Length > 0)
                        {
                            Flush(result, plain, bold, italic);
                            foreach (var sp in Inline(s.Substring(i + 1, close - i - 1), bold, italic, depth + 1))
                                AddSpan(result, sp with { Link = url });
                            i = end + 1;
                            continue;
                        }
                    }
                }
                plain.Append(c);
                i++;
                continue;
            }

            if ((c == '*' || c == '_') && depth < MaxDepth)
            {
                int run = CountRun(s, i, c);
                if (run >= 2 && CanOpen(s, i, c, 2))
                {
                    if (run >= 3 && CanOpen(s, i, c, 3))
                    {
                        int j = FindCloser(s, i + 3, c, 3);
                        if (j >= 0)
                        {
                            Flush(result, plain, bold, italic);
                            foreach (var sp in Inline(s.Substring(i + 3, j - i - 3), true, true, depth + 1)) AddSpan(result, sp);
                            i = j + 3;
                            continue;
                        }
                    }
                    int k = FindCloser(s, i + 2, c, 2);
                    if (k >= 0)
                    {
                        Flush(result, plain, bold, italic);
                        foreach (var sp in Inline(s.Substring(i + 2, k - i - 2), true, italic, depth + 1)) AddSpan(result, sp);
                        i = k + 2;
                        continue;
                    }
                }
                else if (run == 1 && CanOpen(s, i, c, 1))
                {
                    int k = FindCloser(s, i + 1, c, 1);
                    if (k >= 0)
                    {
                        Flush(result, plain, bold, italic);
                        foreach (var sp in Inline(s.Substring(i + 1, k - i - 1), bold, true, depth + 1)) AddSpan(result, sp);
                        i = k + 1;
                        continue;
                    }
                }
                // Unmatched delimiter run: keep it literal.
                plain.Append(c, run);
                i += run;
                continue;
            }

            plain.Append(c);
            i++;
        }
        Flush(result, plain, bold, italic);
        return result;
    }

    private static void Flush(List<MdSpan> result, StringBuilder plain, bool bold, bool italic)
    {
        if (plain.Length == 0) return;
        AddSpan(result, new MdSpan(plain.ToString(), bold, italic));
        plain.Clear();
    }

    private static void AddSpan(List<MdSpan> list, MdSpan span)
    {
        if (span.Text.Length == 0) return;
        if (list.Count > 0)
        {
            var last = list[^1];
            if (!span.Code && !last.Code && span.Link is null && last.Link is null
                && last.Bold == span.Bold && last.Italic == span.Italic)
            {
                list[^1] = last with { Text = last.Text + span.Text };
                return;
            }
        }
        list.Add(span);
    }

    private static int CountRun(string s, int i, char c)
    {
        int j = i;
        while (j < s.Length && s[j] == c) j++;
        return j - i;
    }

    private static int FindBacktickRun(string s, int from, int len)
    {
        int j = from;
        while (j < s.Length)
        {
            if (s[j] == '`')
            {
                int r = CountRun(s, j, '`');
                if (r == len) return j;
                j += r;
            }
            else
            {
                j++;
            }
        }
        return -1;
    }

    private static bool CanOpen(string s, int i, char c, int len)
    {
        int after = i + len;
        if (after >= s.Length || char.IsWhiteSpace(s[after])) return false;
        return c != '_' || i == 0 || !char.IsLetterOrDigit(s[i - 1]);
    }

    private static int FindCloser(string s, int from, char c, int len)
    {
        int j = from;
        while (j < s.Length)
        {
            if (s[j] != c) { j++; continue; }
            int r = CountRun(s, j, c);
            if (r == len
                && j > from
                && !char.IsWhiteSpace(s[j - 1])
                && (c == '*' || j + len >= s.Length || !char.IsLetterOrDigit(s[j + len])))
            {
                return j;
            }
            j += r;
        }
        return -1;
    }

    private static bool IsAsciiPunct(char ch) => ch > 32 && ch < 127 && !char.IsLetterOrDigit(ch);

    private static string StripCodePadding(string s) =>
        s.Length >= 2 && s[0] == ' ' && s[^1] == ' ' && s.Trim().Length > 0 ? s[1..^1] : s;

    private static string ExtractUrl(string raw)
    {
        string u = raw.Trim();
        if (u.Length >= 2 && u[0] == '<' && u[^1] == '>') u = u[1..^1];
        int ws = u.IndexOfAny(new[] { ' ', '\t' });
        if (ws >= 0) u = u[..ws];
        return u.Trim();
    }
}
