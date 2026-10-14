namespace OrchDesk.Core.Tools;

public sealed class PathOutsideWorkspaceException(string message) : Exception(message);

/// <summary>
/// The single gate every file tool goes through. A model-supplied path is resolved against the chosen folder and
/// rejected if it escapes it: via "..", an absolute path elsewhere, or a symlink/junction pointing outside.
/// </summary>
public static class WorkspacePath
{
    private static StringComparison Cmp =>
        OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

    public static string NormalizeRoot(string root) =>
        Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));

    public static bool IsInside(string root, string fullPath)
    {
        var r = NormalizeRoot(root);
        var p = Path.TrimEndingDirectorySeparator(Path.GetFullPath(fullPath));
        return p.Equals(r, Cmp) || p.StartsWith(r + Path.DirectorySeparatorChar, Cmp);
    }

    public static string Resolve(string root, string? relativePath)
    {
        var r = NormalizeRoot(root);
        var rel = string.IsNullOrWhiteSpace(relativePath) ? "." : relativePath.Trim();

        // Reject ".." segments outright, even ones that would stay inside after normalisation: it keeps the rule simple.
        var segments = rel.Split('/', '\\');
        if (segments.Any(s => s == ".."))
            throw new PathOutsideWorkspaceException($"Path '{relativePath}' uses '..', which is not allowed.");

        var full = Path.GetFullPath(Path.IsPathRooted(rel) ? rel : Path.Combine(r, rel));
        if (!IsInside(r, full))
            throw new PathOutsideWorkspaceException($"Path '{relativePath}' is outside the project folder.");

        // Walk from the target up to the root; any existing link along the way must also resolve inside.
        for (var cur = full; cur is not null && IsInside(r, cur) && !cur.Equals(r, Cmp); cur = Path.GetDirectoryName(cur))
        {
            FileSystemInfo info = Directory.Exists(cur) ? new DirectoryInfo(cur) : new FileInfo(cur);
            if (!info.Exists || info.LinkTarget is null) continue;
            var target = info.ResolveLinkTarget(returnFinalTarget: true);
            if (target is not null && !IsInside(r, target.FullName))
                throw new PathOutsideWorkspaceException($"Path '{relativePath}' goes through a link that points outside the project folder.");
        }
        return full;
    }

    public static string ToRelative(string root, string fullPath) =>
        Path.GetRelativePath(NormalizeRoot(root), fullPath).Replace('\\', '/');
}
