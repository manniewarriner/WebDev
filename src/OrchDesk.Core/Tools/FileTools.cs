using System.Text;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace OrchDesk.Core.Tools;

internal static class FileWalk
{
    public static readonly HashSet<string> IgnoredDirs = new(StringComparer.OrdinalIgnoreCase)
        { ".git", "bin", "obj", "node_modules", ".vs", ".idea", "packages", "dist", "build", ".next", "__pycache__", ".venv" };

    public static IEnumerable<string> Files(string dir, bool recursive)
    {
        var stack = new Stack<string>([dir]);
        while (stack.Count > 0)
        {
            var cur = stack.Pop();
            string[] files, dirs;
            try { files = Directory.GetFiles(cur); dirs = Directory.GetDirectories(cur); }
            catch (UnauthorizedAccessException) { continue; }
            foreach (var f in files.Order(StringComparer.OrdinalIgnoreCase)) yield return f;
            if (!recursive) continue;
            foreach (var d in dirs.OrderDescending(StringComparer.OrdinalIgnoreCase))
                if (!IgnoredDirs.Contains(Path.GetFileName(d)) && new DirectoryInfo(d).LinkTarget is null) stack.Push(d);
        }
    }

    public static bool LooksBinary(string path)
    {
        Span<byte> buf = stackalloc byte[4096];
        using var fs = File.OpenRead(path);
        var n = fs.Read(buf);
        return buf[..n].Contains((byte)0);
    }
}

public sealed class ListFilesTool : ITool
{
    public string Name => "list_files";
    public string Description => "List files in the project (relative paths). Skips bin/obj/.git/node_modules.";
    public JsonObject Parameters => ToolArgs.Schema(
        ("path", "string", "Folder relative to the project root (default: root).", false),
        ("recursive", "boolean", "Include subfolders (default true).", false));

    public Task<string> ExecuteAsync(JsonObject args, ToolContext ctx, CancellationToken ct)
    {
        var dir = WorkspacePath.Resolve(ctx.WorkspaceRoot, ToolArgs.OptStr(args, "path"));
        if (!Directory.Exists(dir)) throw new ToolException($"Folder not found: {ToolArgs.OptStr(args, "path")}");
        var recursive = args["recursive"] is null || ToolArgs.OptBool(args, "recursive");
        const int max = 500;
        var files = FileWalk.Files(dir, recursive).Take(max + 1).Select(f => WorkspacePath.ToRelative(ctx.WorkspaceRoot, f)).ToList();
        var sb = new StringBuilder(string.Join('\n', files.Take(max)));
        if (files.Count > max) sb.Append($"\n…(more than {max} files; narrow the path)");
        return Task.FromResult(files.Count == 0 ? "(no files)" : sb.ToString());
    }
}

public sealed class ReadFileTool : ITool
{
    private const int MaxChars = 200_000;
    public string Name => "read_file";
    public string Description => "Read a text file. Returns numbered lines. Use offset/limit for large files.";
    public JsonObject Parameters => ToolArgs.Schema(
        ("path", "string", "File path relative to the project root.", true),
        ("offset", "integer", "First line to return (1-based).", false),
        ("limit", "integer", "Maximum number of lines.", false));

    public async Task<string> ExecuteAsync(JsonObject args, ToolContext ctx, CancellationToken ct)
    {
        var path = WorkspacePath.Resolve(ctx.WorkspaceRoot, ToolArgs.Str(args, "path"));
        if (!File.Exists(path)) return $"File does not exist: {ToolArgs.Str(args, "path")}";
        if (FileWalk.LooksBinary(path)) throw new ToolException("File looks binary; not reading it.");
        var lines = await File.ReadAllLinesAsync(path, ct);
        var offset = Math.Max(1, ToolArgs.OptInt(args, "offset") ?? 1);
        var limit = Math.Max(1, ToolArgs.OptInt(args, "limit") ?? 2000);
        var sb = new StringBuilder();
        for (var i = offset - 1; i < lines.Length && i < offset - 1 + limit; i++)
        {
            sb.Append(i + 1).Append('\t').Append(lines[i]).Append('\n');
            if (sb.Length > MaxChars) { sb.Append($"…(truncated at line {i + 1} of {lines.Length})"); break; }
        }
        return sb.Length == 0 ? "(empty file)" : sb.ToString();
    }
}

public sealed class SearchTool : ITool
{
    public string Name => "search";
    public string Description => "Search file contents with a regular expression (case-insensitive). Returns path:line: text.";
    public JsonObject Parameters => ToolArgs.Schema(
        ("pattern", "string", "Regular expression to find.", true),
        ("path", "string", "Folder to search (default: root).", false),
        ("glob", "string", "Only files whose name matches, e.g. *.cs", false));

    public Task<string> ExecuteAsync(JsonObject args, ToolContext ctx, CancellationToken ct)
    {
        Regex rx;
        try { rx = new Regex(ToolArgs.Str(args, "pattern"), RegexOptions.IgnoreCase, TimeSpan.FromSeconds(2)); }
        catch (ArgumentException ex) { throw new ToolException($"Invalid regex: {ex.Message}"); }
        var dir = WorkspacePath.Resolve(ctx.WorkspaceRoot, ToolArgs.OptStr(args, "path"));
        var glob = ToolArgs.OptStr(args, "glob");
        Regex? globRx = glob is null ? null :
            new Regex("^" + Regex.Escape(glob).Replace(@"\*", ".*").Replace(@"\?", ".") + "$", RegexOptions.IgnoreCase);

        const int maxHits = 200;
        var hits = new List<string>();
        foreach (var file in FileWalk.Files(dir, true))
        {
            ct.ThrowIfCancellationRequested();
            if (globRx is not null && !globRx.IsMatch(Path.GetFileName(file))) continue;
            // Search must not follow file symlinks into an outside folder.
            try { WorkspacePath.Resolve(ctx.WorkspaceRoot, file); }
            catch (PathOutsideWorkspaceException) { continue; }
            var info = new FileInfo(file);
            if (info.Length > 1_000_000 || FileWalk.LooksBinary(file)) continue;
            var n = 0;
            foreach (var line in File.ReadLines(file))
            {
                n++;
                if (!rx.IsMatch(line)) continue;
                hits.Add($"{WorkspacePath.ToRelative(ctx.WorkspaceRoot, file)}:{n}: {(line.Length > 300 ? line[..300] : line).Trim()}");
                if (hits.Count >= maxHits) return Task.FromResult(string.Join('\n', hits) + $"\n…(stopped at {maxHits} matches)");
            }
        }
        return Task.FromResult(hits.Count == 0 ? "No matches." : string.Join('\n', hits));
    }
}

public sealed class WriteFileTool : ITool
{
    public string Name => "write_file";
    public string Description => "Create or overwrite a file with the given content. Creates folders as needed.";
    public JsonObject Parameters => ToolArgs.Schema(
        ("path", "string", "File path relative to the project root.", true),
        ("content", "string", "Full file content.", true));

    public async Task<string> ExecuteAsync(JsonObject args, ToolContext ctx, CancellationToken ct)
    {
        var path = WorkspacePath.Resolve(ctx.WorkspaceRoot, ToolArgs.Str(args, "path"));
        var content = ToolArgs.Str(args, "content");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var existed = File.Exists(path);
        await File.WriteAllTextAsync(path, content, ct);
        return $"{(existed ? "Overwrote" : "Created")} {ToolArgs.Str(args, "path")} ({content.Length} chars).";
    }
}

/// <summary>
/// Search/replace edit: the format small local models get right most often. old_string must match exactly once
/// (unless replace_all), so an ambiguous edit fails loudly instead of changing the wrong place.
/// </summary>
public sealed class EditFileTool : ITool
{
    public string Name => "edit_file";
    public string Description =>
        "Replace exact text in a file. old_string must appear exactly once (include surrounding lines to make it unique) " +
        "unless replace_all is true. Use write_file to create new files.";
    public JsonObject Parameters => ToolArgs.Schema(
        ("path", "string", "File path relative to the project root.", true),
        ("old_string", "string", "Exact existing text to replace.", true),
        ("new_string", "string", "Replacement text.", true),
        ("replace_all", "boolean", "Replace every occurrence.", false));

    public async Task<string> ExecuteAsync(JsonObject args, ToolContext ctx, CancellationToken ct)
    {
        var rel = ToolArgs.Str(args, "path");
        var path = WorkspacePath.Resolve(ctx.WorkspaceRoot, rel);
        if (!File.Exists(path)) throw new ToolException($"File not found: {rel}");
        var oldStr = ToolArgs.Str(args, "old_string");
        var newStr = ToolArgs.Str(args, "new_string");
        if (oldStr.Length == 0) throw new ToolException("old_string must not be empty.");

        var text = await File.ReadAllTextAsync(path, ct);
        // Models often send \n where the file has \r\n; normalise for matching but keep the file's style.
        var crlf = text.Contains("\r\n");
        if (crlf) { oldStr = oldStr.Replace("\r\n", "\n").Replace("\n", "\r\n"); newStr = newStr.Replace("\r\n", "\n").Replace("\n", "\r\n"); }

        var count = CountOccurrences(text, oldStr);
        if (count == 0 && !ToolArgs.OptBool(args, "replace_all")) {
            try { var tolerant = TextEdits.Apply(text, oldStr, newStr); await File.WriteAllTextAsync(path, tolerant, ct); return $"Edited {rel} (1 replacement)."; }
            catch (InvalidOperationException e) { throw new ToolException(e.Message); }
        }
        if (count == 0) throw new ToolException($"old_string not found in {rel}. Read the file again and copy the text exactly.");
        var all = ToolArgs.OptBool(args, "replace_all");
        if (count > 1 && !all) throw new ToolException($"old_string appears {count} times in {rel}. Add more context or set replace_all.");

        var updated = all ? text.Replace(oldStr, newStr, StringComparison.Ordinal) : ReplaceFirst(text, oldStr, newStr);
        await File.WriteAllTextAsync(path, updated, ct);
        return $"Edited {rel} ({(all ? count : 1)} replacement{(all && count > 1 ? "s" : "")}).";
    }

    public static int CountOccurrences(string text, string value)
    {
        int count = 0, i = 0;
        while ((i = text.IndexOf(value, i, StringComparison.Ordinal)) >= 0) { count++; i += value.Length; }
        return count;
    }

    private static string ReplaceFirst(string text, string oldStr, string newStr)
    {
        var i = text.IndexOf(oldStr, StringComparison.Ordinal);
        return string.Concat(text.AsSpan(0, i), newStr, text.AsSpan(i + oldStr.Length));
    }
}

public sealed class DeleteFileTool : ITool
{
    public string Name => "delete_file";
    public string Description => "Delete a file. Always requires user approval.";
    public JsonObject Parameters => ToolArgs.Schema(("path", "string", "File path relative to the project root.", true));

    public Task<string> ExecuteAsync(JsonObject args, ToolContext ctx, CancellationToken ct)
    {
        var rel = ToolArgs.Str(args, "path");
        var path = WorkspacePath.Resolve(ctx.WorkspaceRoot, rel);
        if (!File.Exists(path)) throw new ToolException($"File not found: {rel}");
        File.Delete(path);
        return Task.FromResult($"Deleted {rel}.");
    }
}
