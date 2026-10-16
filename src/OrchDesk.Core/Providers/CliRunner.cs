using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Threading.Channels;

namespace OrchDesk.Core.Providers;

public sealed record CliLine(string Text, bool IsStdErr);

/// <summary>Starts a CLI process and streams its output lines. Cancelling kills the whole process tree.</summary>
public static class CliRunner
{
    /// <summary>Environment variables that would make the Claude CLI bill API credits instead of the subscription.</summary>
    public static readonly string[] PaidApiEnvVars =
        ["ANTHROPIC_API_KEY", "ANTHROPIC_AUTH_TOKEN", "ANTHROPIC_BASE_URL", "CLAUDE_CODE_USE_BEDROCK", "CLAUDE_CODE_USE_VERTEX"];

    public static ProcessStartInfo CreateStartInfo(
        string fileName, IEnumerable<string> args, string? workingDirectory,
        IReadOnlyDictionary<string, string?>? env = null)
    {
        var psi = new ProcessStartInfo
        {
            FileName = fileName,
            WorkingDirectory = workingDirectory ?? Environment.CurrentDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardOutputEncoding = System.Text.Encoding.UTF8,
            StandardErrorEncoding = System.Text.Encoding.UTF8,
        };
        var arguments = args.ToList();
        if (OperatingSystem.IsWindows() && Path.GetExtension(fileName).ToLowerInvariant() is ".cmd" or ".bat")
        {
            // npm CLIs use batch shims. Prompts stay on stdin and are never parsed by cmd.
            var values = new[] { fileName }.Concat(arguments).ToList();
            if (values.Any(v => v.IndexOfAny(['"', '%', '!', '^', '&', '|', '<', '>', '\r', '\n']) >= 0))
                throw new ArgumentException("Unsafe character in CLI executable or option");
            psi.FileName = Environment.GetEnvironmentVariable("COMSPEC") ?? "cmd.exe";
            psi.Arguments = "/d /s /c \"" + string.Join(" ", values.Select(v => "\"" + v + "\"")) + "\"";
        }
        else foreach (var a in arguments) psi.ArgumentList.Add(a);
        // ArgumentList quotes each argument safely, so prompts with spaces/quotes can't break the command line.

        if (env is not null)
        {
            foreach (var (k, v) in env)
            {
                if (v is null) psi.Environment.Remove(k);
                else psi.Environment[k] = v;
            }
        }
        OrchDesk.Core.Tools.WorkspaceTemp.Configure(psi);
        return psi;
    }

    /// <param name="stdin">Text written to the process's standard input (prompts go here, not on the command line,
    /// so Windows .cmd shims never re-parse user text).</param>
    public static async IAsyncEnumerable<CliLine> StreamAsync(
        ProcessStartInfo psi, string? stdin, [EnumeratorCancellation] CancellationToken ct)
    {
        using var process = new Process { StartInfo = psi, EnableRaisingEvents = true };
        var channel = Channel.CreateUnbounded<CliLine>();
        var open = 2;
        void Complete() { if (Interlocked.Decrement(ref open) == 0) channel.Writer.TryComplete(); }

        process.OutputDataReceived += (_, e) => { if (e.Data is null) Complete(); else channel.Writer.TryWrite(new CliLine(e.Data, false)); };
        process.ErrorDataReceived += (_, e) => { if (e.Data is null) Complete(); else channel.Writer.TryWrite(new CliLine(e.Data, true)); };

        process.Start();
        using var ownership = ChildProcesses.Track(process);
        using var reg = ct.Register(() => { try { if (!process.HasExited) process.Kill(entireProcessTree: true); } catch { } });
        // Start reading before writing stdin so a chatty process can't fill its output pipe and deadlock us.
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        if (stdin is not null) await process.StandardInput.WriteAsync(stdin.AsMemory(), ct);
        process.StandardInput.Close();

        await foreach (var line in channel.Reader.ReadAllAsync(ct))
        {
            if (ct.IsCancellationRequested) break;
            yield return line;
        }
        try { await process.WaitForExitAsync(CancellationToken.None); } catch { }
        ct.ThrowIfCancellationRequested();
        if (process.ExitCode != 0)
            yield return new CliLine($"[exit code {process.ExitCode}]", true);
    }

    /// <summary>Finds an executable on PATH (checks .exe/.cmd/.bat on Windows). Returns null if missing.</summary>
    public static string? FindOnPath(string name)
    {
        if (Path.IsPathRooted(name)) return File.Exists(name) ? name : null;
        var exts = OperatingSystem.IsWindows() ? new[] { ".exe", ".cmd", ".bat", "" } : new[] { "" };
        foreach (var dir in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator))
        {
            if (string.IsNullOrWhiteSpace(dir)) continue;
            foreach (var ext in exts)
            {
                var candidate = Path.Combine(dir, name + ext);
                if (File.Exists(candidate)) return candidate;
            }
        }
        return null;
    }
}
