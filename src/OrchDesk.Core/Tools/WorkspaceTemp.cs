using System.Diagnostics;

namespace OrchDesk.Core.Tools;

public static class WorkspaceTemp
{
    public static void Configure(ProcessStartInfo start)
    {
        var root = WorkspacePath.Resolve(start.WorkingDirectory, ".orchdesk-tmp");
        Directory.CreateDirectory(root);
        var ignore = WorkspacePath.Resolve(start.WorkingDirectory, ".orchdesk-tmp/.gitignore");
        File.WriteAllText(ignore, "*\n");
        var pytest = WorkspacePath.Resolve(start.WorkingDirectory, ".orchdesk-tmp/pytest");
        Directory.CreateDirectory(pytest);
        foreach (var name in new[] { "TMP", "TEMP", "TMPDIR" }) start.Environment[name] = root;
        start.Environment["PYTEST_ADDOPTS"] = "--basetemp=\"" + pytest + "\"";
    }

    public static void Clean(string workspace)
    {
        try
        {
            var root = WorkspacePath.Resolve(workspace, ".orchdesk-tmp");
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or PathOutsideWorkspaceException) { }
    }
}
