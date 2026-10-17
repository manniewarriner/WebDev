namespace OrchDesk.Core.Orchestration;

// Role instructions from the original build's orchestra. The ideas (severity-ranked review, security checklist, test-first work and a
// minimal-diff build repair) are adapted from the MIT-licensed affaan-m/ECC agent collection; the wording is OrchDesk's own.
// Managed Auto appends them to its planner, worker and reviewer prompts.
public static class Roles
{
    public const string WorkerRules = """
Working rules:
- Make the smallest change that completes the task. Follow the patterns already in the project. Do not refactor or rename unrelated code.
- For code changes, add or update a test that fails without your change and passes with it. Cover empty or null input, boundaries and error paths.
- Security: never write secrets, keys or tokens into code or logs; validate external input; use parameterised queries; do not build file paths or shell commands from untrusted text; do not leak internals in error messages.
- Do not leave debug output, commented-out code or TODOs behind.
""";

    public const string ReviewRules = """
Review rules:
- Judge the result against the task, then check for correctness bugs, missing tests for changed behaviour, and security problems (injection, secrets in code, unvalidated input, unsafe file/URL/shell handling, leaking errors).
- Rate every finding CRITICAL (security or data loss), HIGH (wrong or missing behaviour), MEDIUM or LOW (style, performance). Only report findings you can prove from the code, with file and line.
- Fail only when there is at least one CRITICAL or HIGH finding. MEDIUM and LOW findings do not fail the work: pass it and list them as notes.
- When failing, give a numbered list of fixes, each as "file:line - exact change".
""";

    public const string PlanRules = """
Plan quality: every chunk must name the files it will touch, have a result someone can check, and stay independently mergeable. For code changes include a test in the same chunk. Prefer several small chunks over one large one when they touch different files.
""";

    public static string RepairPrompt(string taskTitle, string buildOutput) => $"""
You are fixing a failing build. Your change for the task "{taskTitle}" left the project's build/test check failing.
Fix every error below with the smallest possible diff. Do not refactor, rename or change behaviour beyond what the errors require.
CHECK OUTPUT:
{(buildOutput.Length > 6000 ? buildOutput[^6000..] : buildOutput)}
Reply with a short summary of exactly what you changed.
""";
}
