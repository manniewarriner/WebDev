using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace OrchDesk.Core.Permissions;

public enum Verdict { Allow, Ask, Deny }

public sealed record PermissionDecision(Verdict Verdict, string Reason)
{
    public static PermissionDecision Allow(string reason = "allowed") => new(Verdict.Allow, reason);
    public static PermissionDecision Ask(string reason) => new(Verdict.Ask, reason);
    public static PermissionDecision Deny(string reason) => new(Verdict.Deny, reason);
}

/// <summary>
/// Decides, before a tool runs, whether it is safe to do automatically, needs the user's OK, or is refused.
///   Allow: reading, editing files inside the folder, build/test commands, local git (status/diff/commit).
///   Ask:   deleting, git push, package installs, network commands, anything else on the shell.
///   Deny:  in unattended (overnight) mode, anything that would have asked, plus pushes to main/master.
/// </summary>
public sealed partial class PermissionPolicy
{
    private static readonly HashSet<string> AlwaysAllowedTools =
        ["list_files", "read_file", "search", "write_file", "edit_file", "git_status", "git_diff", "git_commit", "web_fetch"];

    /// <summary>Extra command prefixes the user has allowed (e.g. "npm run lint"), from settings.</summary>
    public List<string> ExtraAllowedCommandPrefixes { get; } = [];

    /// <summary>Overnight mode: there is nobody to ask, so every "Ask" becomes "Deny".</summary>
    public bool Unattended { get; set; }
    public string? WorkspaceRoot { get; set; }
    public OrchDesk.Core.Config.WorkspacePermission? WorkspacePermission { get; set; }
    public bool HostManagedCommits { get; set; }

    public PermissionDecision Evaluate(string toolName, JsonObject args)
    {
        if (HostManagedCommits && toolName is "git_commit" or "git_push") return PermissionDecision.Deny("Overnight host controls commits and never pushes");
        if (WorkspacePermission is not null && toolName == "run_command" && OutsidePath().IsMatch(args["command"]?.ToString() ?? ""))
            return PermissionDecision.Deny("Commands referencing paths outside the workspace are blocked");
        if (WorkspaceRoot is not null && toolName is "write_file" or "edit_file" or "delete_file")
        {
            try { OrchDesk.Core.Tools.WorkspacePath.Resolve(WorkspaceRoot, args["path"]?.GetValue<string>()); }
            catch (Exception ex) when (ex is OrchDesk.Core.Tools.PathOutsideWorkspaceException or ArgumentException) { return PermissionDecision.Deny(ex.Message); }
        }
        var decision = EvaluateAttended(toolName, args);
        if (decision.Verdict != Verdict.Deny && WorkspacePermission is { } mode && toolName is "write_file" or "edit_file" or "delete_file" or "run_command")
            decision = mode == OrchDesk.Core.Config.WorkspacePermission.FullAccess ? PermissionDecision.Allow("Full access") : PermissionDecision.Ask("Ask first");
        return Unattended && decision.Verdict == Verdict.Ask
            ? PermissionDecision.Deny($"{decision.Reason} (unattended mode: not allowed without a person to approve)")
            : decision;
    }

    private PermissionDecision EvaluateAttended(string toolName, JsonObject args)
    {
        if (args["path"] is JsonValue path && path.TryGetValue<string>(out var suppliedPath) &&
            (Path.IsPathRooted(suppliedPath) || suppliedPath.Split('/', '\\').Contains("..")))
            return PermissionDecision.Ask("Absolute or parent path requires review; workspace confinement still applies");
        if (AlwaysAllowedTools.Contains(toolName)) return PermissionDecision.Allow();
        return toolName switch
        {
            "delete_file" => PermissionDecision.Ask($"Delete {args["path"]}"),
            "git_push" => PermissionDecision.Ask("Push this branch to the remote"),
            "run_command" => EvaluateCommand(args["command"]?.GetValue<string>() ?? ""),
            _ => PermissionDecision.Ask($"Unknown tool '{toolName}'"),
        };
    }

    public PermissionDecision EvaluateCommand(string command)
    {
        var cmd = command.Trim();
        if (HostManagedCommits && Regex.IsMatch(cmd, @"\bgit\s+(commit|push|checkout|switch|reset|add)\b", RegexOptions.IgnoreCase)) return PermissionDecision.Deny("Overnight host controls the branch and commits");
        if (cmd.Length == 0) return PermissionDecision.Deny("Empty command");

        // Order matters: risky patterns win even if the command also starts like a test command.
        if (GitPush().IsMatch(cmd)) return PermissionDecision.Ask("git push");
        if (Destructive().IsMatch(cmd)) return PermissionDecision.Ask("Deletes files or rewrites history");
        if (PackageInstall().IsMatch(cmd)) return PermissionDecision.Ask("Installs packages");
        if (Network().IsMatch(cmd)) return PermissionDecision.Ask("Uses the network");
        if (OutsidePath().IsMatch(cmd)) return PermissionDecision.Ask("Touches paths outside the project folder");

        // Chaining (&&, ;, |, >) could hide a second command behind an allowed one, so chained commands always ask.
        if (Chaining().IsMatch(cmd)) return PermissionDecision.Ask("Chained or redirected command");

        if (BuildOrTest().IsMatch(cmd) || ExtraAllowedCommandPrefixes.Any(p => cmd.StartsWith(p, StringComparison.OrdinalIgnoreCase)))
            return PermissionDecision.Allow("build/test command");

        return PermissionDecision.Ask("Shell command");
    }

    [GeneratedRegex(@"^(dotnet\s+(build|test|restore|format|run\s+--project\s+\S*test)|npm\s+(test|run\s+(build|test|lint))|pnpm\s+(test|build|lint)|yarn\s+(test|build|lint)|pytest|python\s+-m\s+pytest|cargo\s+(build|test|check)|go\s+(build|test|vet)|mvn\s+(-q\s+)?(test|verify|package)|gradle(w)?\s+(build|test)|\.?[\\/]?gradlew\s+(build|test)|make(\s+(test|build|check))?|git\s+(status|diff|log|show)\b)(\s|$)", RegexOptions.IgnoreCase)]
    private static partial Regex BuildOrTest();

    [GeneratedRegex(@"\bgit\s+push\b", RegexOptions.IgnoreCase)]
    private static partial Regex GitPush();

    [GeneratedRegex(@"(^|[\s;&|])(rm|del|erase|rmdir|rd|Remove-Item|format|diskpart|shutdown)(\s|$)|git\s+(reset\s+--hard|clean\s+-[a-z]*f|checkout\s+--\s|branch\s+-D|rebase)|--force\b", RegexOptions.IgnoreCase)]
    private static partial Regex Destructive();

    [GeneratedRegex(@"\b(npm|pnpm|yarn)\s+(i|install|add)\b|\bpip3?\s+install\b|\bdotnet\s+(add\s+(\S+\s+)?package|tool\s+install|new\s+install)\b|\b(choco|winget|scoop|apt(-get)?|brew)\s+install\b|\bcargo\s+install\b|\bgo\s+install\b|Install-(Package|Module)", RegexOptions.IgnoreCase)]
    private static partial Regex PackageInstall();

    [GeneratedRegex(@"\b(curl|wget|Invoke-WebRequest|Invoke-RestMethod|iwr|irm|ssh|scp|ftp|nc|ncat|telnet|git\s+(clone|fetch|pull)|Start-BitsTransfer)\b", RegexOptions.IgnoreCase)]
    private static partial Regex Network();

    [GeneratedRegex(@"(^|[\s""'=])(\.\.(?:[\\/]|[""'\s]|$)|[A-Za-z]:[\\/]|~[\\/]|\\\\|/etc/|/usr/|%[A-Za-z_][A-Za-z0-9_]*%|\$env:)", RegexOptions.IgnoreCase)]
    private static partial Regex OutsidePath();

    [GeneratedRegex(@"&&|\|\||[;|`><&\r\n]|\$\(|\^")]
    private static partial Regex Chaining();
}
