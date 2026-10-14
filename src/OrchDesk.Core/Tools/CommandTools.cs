using System.Diagnostics;
using System.Text;
using System.Text.Json.Nodes;

namespace OrchDesk.Core.Tools;

public sealed record CommandResult(int ExitCode, string Output, bool TimedOut);

/// <summary>Runs a process to completion with a timeout, keeping only the tail of its output.</summary>
public static class ProcessRunner
{
    public const int MaxOutputChars = 32_000;

    public static ProcessStartInfo Shell(string command, string cwd)
    {
        var psi = OperatingSystem.IsWindows()
            ? new ProcessStartInfo("cmd.exe") { ArgumentList = { "/d", "/s", "/c", command } }
            : new ProcessStartInfo("/bin/sh") { ArgumentList = { "-c", command } };
        psi.WorkingDirectory = cwd;
        return psi;
    }

    public static ProcessStartInfo Exe(string file, IEnumerable<string> args, string cwd)
    {
        var psi = new ProcessStartInfo(file) { WorkingDirectory = cwd };
        foreach (var a in args) psi.ArgumentList.Add(a);
        return psi;
    }

    public static async Task<CommandResult> RunAsync(ProcessStartInfo psi, TimeSpan timeout, CancellationToken ct)
    {
        psi.RedirectStandardOutput = psi.RedirectStandardError = psi.RedirectStandardInput = true;
        psi.UseShellExecute = false;
        psi.CreateNoWindow = true;
        psi.Environment["GIT_TERMINAL_PROMPT"] = "0"; // never hang waiting for a password

        var output = new StringBuilder();
        var gate = new object();
        void Append(string? line)
        {
            if (line is null) return;
            lock (gate)
            {
                output.Append(line).Append('\n');
                if (output.Length > MaxOutputChars * 2) output.Remove(0, output.Length - MaxOutputChars);
            }
        }

        using var p = new Process { StartInfo = psi };
        p.OutputDataReceived += (_, e) => Append(e.Data);
        p.ErrorDataReceived += (_, e) => Append(e.Data);
        try { p.Start(); }
        catch (System.ComponentModel.Win32Exception ex) { return new CommandResult(-1, $"Could not start '{psi.FileName}': {ex.Message}", false); }
        p.StandardInput.Close();
        p.BeginOutputReadLine();
        p.BeginErrorReadLine();

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutCts.CancelAfter(timeout);
        var timedOut = false;
        try { await p.WaitForExitAsync(timeoutCts.Token); }
        catch (OperationCanceledException)
        {
            timedOut = !ct.IsCancellationRequested;
            try { p.Kill(entireProcessTree: true); } catch { }
            try { await p.WaitForExitAsync(CancellationToken.None); } catch { }
            ct.ThrowIfCancellationRequested();
        }
        p.WaitForExit(); // flush the async output readers

        string text;
        lock (gate) text = output.ToString();
        if (text.Length > MaxOutputChars) text = "…(earlier output truncated)\n" + text[^MaxOutputChars..];
        return new CommandResult(timedOut ? -1 : p.ExitCode, text, timedOut);
    }

    public static string Format(CommandResult r) =>
        (r.TimedOut ? "TIMED OUT\n" : $"Exit code: {r.ExitCode}\n") + (r.Output.Length == 0 ? "(no output)" : r.Output);
}

public sealed class RunCommandTool : ITool
{
    public string Name => "run_command";
    public string Description =>
        "Run a shell command in the project folder (cmd on Windows) and return exit code and output. " +
        "Use for builds and tests, e.g. 'dotnet test'. Default timeout 120 s, max 600 s.";
    public JsonObject Parameters => ToolArgs.Schema(
        ("command", "string", "The command line to run.", true),
        ("timeout_seconds", "integer", "Timeout in seconds (default 120, max 600).", false));

    public async Task<string> ExecuteAsync(JsonObject args, ToolContext ctx, CancellationToken ct)
    {
        var command = ToolArgs.Str(args, "command");
        var seconds = Math.Clamp(ToolArgs.OptInt(args, "timeout_seconds") ?? (int)ctx.DefaultCommandTimeout.TotalSeconds, 1, 600);
        var result = await ProcessRunner.RunAsync(ProcessRunner.Shell(command, ctx.WorkspaceRoot), TimeSpan.FromSeconds(seconds), ct);
        return ProcessRunner.Format(result);
    }
}

public class GitTool(string name, string description, string[] gitArgs) : ITool
{
    public string Name => name;
    public string Description => description;
    public virtual JsonObject Parameters => ToolArgs.Schema();

    protected virtual IEnumerable<string> BuildArgs(JsonObject args) => gitArgs;

    public virtual async Task<string> ExecuteAsync(JsonObject args, ToolContext ctx, CancellationToken ct)
    {
        var result = await ProcessRunner.RunAsync(ProcessRunner.Exe("git", BuildArgs(args), ctx.WorkspaceRoot), TimeSpan.FromMinutes(2), ct);
        return ProcessRunner.Format(result);
    }
}

public sealed class GitDiffTool() : GitTool("git_diff", "Show uncommitted changes (git diff). Set staged=true for staged changes.", [])
{
    public override JsonObject Parameters => ToolArgs.Schema(
        ("path", "string", "Limit to this file or folder.", false),
        ("staged", "boolean", "Show staged changes instead.", false));

    protected override IEnumerable<string> BuildArgs(JsonObject args)
    {
        var list = new List<string> { "--no-pager", "diff" };
        if (ToolArgs.OptBool(args, "staged")) list.Add("--staged");
        if (ToolArgs.OptStr(args, "path") is { } p) list.AddRange(["--", p]);
        return list;
    }

    public override Task<string> ExecuteAsync(JsonObject args, ToolContext ctx, CancellationToken ct)
    {
        if (ToolArgs.OptStr(args, "path") is { } p) WorkspacePath.Resolve(ctx.WorkspaceRoot, p);
        return base.ExecuteAsync(args, ctx, ct);
    }
}

public sealed class GitCommitTool() : GitTool("git_commit", "Stage all changes (git add -A) and commit them with a message.", [])
{
    public override JsonObject Parameters => ToolArgs.Schema(("message", "string", "Commit message.", true));

    public override async Task<string> ExecuteAsync(JsonObject args, ToolContext ctx, CancellationToken ct)
    {
        var message = ToolArgs.Str(args, "message");
        var add = await ProcessRunner.RunAsync(ProcessRunner.Exe("git", ["add", "-A"], ctx.WorkspaceRoot), TimeSpan.FromMinutes(1), ct);
        if (add.ExitCode != 0) return "git add failed:\n" + add.Output;
        var commit = await ProcessRunner.RunAsync(ProcessRunner.Exe("git", ["commit", "-m", message], ctx.WorkspaceRoot), TimeSpan.FromMinutes(1), ct);
        return ProcessRunner.Format(commit);
    }
}
