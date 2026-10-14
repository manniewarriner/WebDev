# Overnight / Autonomous Workflow Orchestration for OrchDesk (+ user's GitHub repos)

Research date: 2026-10-14. Repo metadata is from the GitHub search API, pulled on that date. Licences marked "(API)" were confirmed through the API. Licences marked "(verify)" come from prior knowledge and could not be checked here, because the minimal search output leaves out the licence field and direct `gh api repos/...` calls were blocked in this session.

## 1. Open-source tools that run coding agents unattended or in parallel

### Takeaway
No existing tool is a drop-in fit for a .NET WPF app. The parallel-agent managers (claude-squad, ccmanager, Crystal/Nimbalyst, vibe-kanban) are all Go, TypeScript or Rust UIs built on one shared pattern: **one git worktree and branch per task, with the agent CLI running headless inside it**. OrchDesk should copy that pattern natively in C# and call each CLI's headless mode (`claude -p --output-format stream-json`, `codex exec --json`, `opencode run`). Integrating these tools as libraries is the wrong approach. Reuse their ideas. Only Backlog.md or Taskmaster are worth considering as interoperable task-file formats.

### Cited Findings
**Parallel/unattended agent managers**
- **smtg-ai/claude-squad**: Go, **AGPL-3.0 (API)**, about 8.6k stars, last push 2026-08-20, not archived. Described as "Manage multiple AI terminal agents like Claude Code, Codex, OpenCode, and Amp". Uses tmux sessions plus a git worktree per task. — [GitHub](https://github.com/smtg-ai/claude-squad); [vibecodinghub summary](https://vibecodinghub.org/tools/claude-squad)
  - Integration mode: reference design only. AGPL plus tmux (no native Windows support) makes embedding a poor fit.
- **BloopAI/vibe-kanban**: Rust, **Apache-2.0 (API)**, about 28.4k stars, last push 2026-09-19, 543 open issues. "Get 10X more out of Claude Code, Codex or any coding agent". A kanban board where each task gets a workspace with worktree isolation, running Claude Code, Codex or Gemini CLI. — [GitHub](https://github.com/BloopAI/vibe-kanban); [jimmysong.io](https://jimmysong.io/ai/vibe-kanban)
  - Integration mode: the permissive licence makes it the best project to borrow code or ideas from (task state machine, executor abstraction per agent). It could also run as a sidecar, but that duplicates OrchDesk's purpose.
- **kbwo/ccmanager**: TypeScript, about 1.26k stars, repo updated 2026-10-14. "Coding Agent Session Manager for Claude Code / Gemini CLI / Codex CLI / Cursor Agent / Copilot CLI / Cline CLI / OpenCode / Kimi CLI". Licence MIT (verify). — [GitHub](https://github.com/kbwo/ccmanager)
- **stravu/crystal**: TypeScript/Electron, about 3.1k stars. The description now reads "(Crystal is now Nimbalyst) Run multiple Codex and Claude Code AI sessions in parallel git worktrees". **Risk: rebranded and possibly being superseded**, so treat it as a reference only. Licence MIT (verify). — [GitHub](https://github.com/stravu/crystal)
- Other worktree-per-agent desktop apps mentioned by aggregators: Emdash and "Parallel Code". These are unverified, aggregator-only sources. — [nevercodealone glossary](https://nevercodealone.de/glossare/ki-tools-2026/git-worktrees-parallele-ki-coding-sessions-2026)
- **OpenHands/OpenHands**: about 90.9k stars, active (updated 2026-10-14). The org now also ships `OpenHands/software-agent-sdk` (Python, about 1.26k stars), `OpenHands/OpenHands-CLI` and `OpenHands/automation`. Licence MIT for the core (verify; an `enterprise` repo exists separately). — [GitHub](https://github.com/OpenHands/OpenHands); [software-agent-sdk](https://github.com/OpenHands/software-agent-sdk)
  - Integration mode: heavy Python/Docker runtime. Usable as an optional "cloud/sandboxed" backend, not as the core engine.
- **sweepai/sweep**: the description is now "Sweep: AI coding assistant for JetBrains". It pivoted away from being the autonomous GitHub-issue-to-PR bot. **Not relevant for overnight orchestration.** — [GitHub](https://github.com/sweepai/sweep)

**Task-file / backlog formats (interop candidates)**
- **eyaltoledano/claude-task-master** (Taskmaster AI): JavaScript, about 28.2k stars, **licence field "Other/NOASSERTION" (API)**. Its README licence is MIT with Commons Clause (verify). **Last push 2026-04-28, so activity has slowed (about 5.5 months without a push).** Homepage now points to tryhamster.com. — [GitHub](https://github.com/eyaltoledano/claude-task-master)
  - Risk: non-OSI licence plus a stall. At most, read its `tasks.json` format.
- **MrLesk/Backlog.md**: TypeScript, about 7.0k stars, updated 2026-10-14. "A tool for managing project collaboration between humans and AI Agents in a git ecosystem". Tasks are stored as markdown files in the repo. Licence MIT (verify). — [GitHub](https://github.com/MrLesk/Backlog.md)
  - Integration mode: OrchDesk could read and write Backlog.md-style markdown task files as its human-editable queue format. Good fit for "queue tasks before bed".

**"Ralph Wiggum" loop**
- Created by Geoffrey Huntley in mid-2025 as a bash loop that re-feeds the same prompt. Progress is kept in files and git rather than in the context window. Anthropic ships a Claude Code plugin (`/ralph-loop` with a completion promise and an iteration cap) that uses a Stop hook to block exit until a completion marker appears. Sources recommend always setting max-iterations (for example 20 to 50) and running in sandboxes or disposable VMs, because it is often paired with `--dangerously-skip-permissions`. — [atcyrus.com](https://www.atcyrus.com/stories/ralph-wiggum-technique-claude-code-autonomous-loops); [VentureBeat](https://venturebeat.com/ai/how-ralph-wiggum-went-from-the-simpsons-to-the-biggest-name-in-ai-right-now); [claudefa.st](https://claudefa.st/blog/guide/mechanics/ralph-wiggum-technique)
- Cost claims in blogs (for example "$50K contract for $297") are anecdotal and unverified. — [atcyrus.com](https://www.atcyrus.com/stories/ralph-wiggum-technique-claude-code-autonomous-loops)

**Headless CLI contracts OrchDesk should drive (primary integration mode)**
- Claude Code: `claude -p` with `--output-format json|stream-json`. JSON output includes `result`, `session_id`, `total_cost_usd` and a per-model cost breakdown, and the exit code is non-zero on failure. `--resume <session_id>` continues a specific session. `--bare` skips hooks, skills, MCP and CLAUDE.md auto-discovery for reproducible runs, **but bare mode does not use subscription OAuth login and needs `ANTHROPIC_API_KEY`**. — [Claude Code headless docs](https://code.claude.com/docs/en/headless)
- Claude Code unattended permissions: `--permission-mode dontAsk|acceptEdits|auto` and `--allowedTools` with rule syntax (for example `Bash(git commit *)`). `--permission-prompts none` (v2.1.259+) denies anything that would prompt and tells Claude not to retry. Denials show up as `permission_denied` messages and `permission_denials` in the result. — [Claude Code headless docs](https://code.claude.com/docs/en/headless)
- Claude Code stream events useful for retries: `system/api_retry` carries `attempt`, `max_retries`, `retry_delay_ms`, `error_status` and `error` (values include `rate_limit`, `overloaded`, `billing_error`, `authentication_failed`). SIGTERM gives exit code 143 and leaves the turn unfinished. Send SIGINT to end the turn cleanly. — [Claude Code headless docs](https://code.claude.com/docs/en/headless)
- Agent SDK `SDKRateLimitEvent`: `{type:"rate_limit_event", rate_limit_info:{status:"allowed"|"allowed_warning"|"rejected", resetsAt?, utilization?, errorCode?:"credits_required", ...}}`. `credits_required` means the claude.ai subscription's included usage is exhausted (v2.1.181+). Result subtypes include `error_max_turns`, `error_during_execution` and `error_max_budget_usd`. Options include `maxTurns` and `maxBudgetUsd` (a client-side estimate that can overshoot, so leave headroom). — [Agent SDK TS reference](https://code.claude.com/docs/en/agent-sdk/typescript)
- `--max-budget-usd` CLI cap also stops background work. — [Claude Code headless docs](https://code.claude.com/docs/en/headless)
- Codex CLI: `codex exec` is the non-interactive mode. `--json` emits JSONL. `--sandbox workspace-write` is recommended, and `--full-auto` is deprecated. Resume with `codex exec resume --last`. A git repo is required unless `--skip-git-repo-check` is passed. — [OpenAI Codex non-interactive docs](https://developers.openai.com/codex/noninteractive.md)
- OpenCode: `opencode run "<msg>"` runs a one-shot task. `-c`/`--continue` and `-s`/`--session <id>` resume sessions. `opencode serve --port 4096` plus `opencode run --attach http://localhost:4096` reuses one warm server. — [glukhov.org quickstart](https://glukhov.org/ai-devtools/opencode/); [e2b docs](https://e2b.dev/docs/agents/opencode) (secondary sources)

### Inferences
- Recommended core loop for OrchDesk, per task:
  1. `git worktree add ../wt/<task-id> -b orchdesk/<date>/<task-id>`
  2. Run the chosen CLI headless with stream-json, persisting `session_id`.
  3. Run the test command.
  4. If tests fail and iterations are under N, re-prompt with the failure output (a bounded Ralph loop).
  5. Commit to the task branch.
  6. Run the secret scan.
  7. Optionally push the branch or open a draft PR.
  8. Remove the worktree, or keep it for review.
- Model rotation trigger: parse `rate_limit_event.status=="rejected"` / `errorCode=="credits_required"` and `api_retry.error=="rate_limit"` from the Claude stream. Then mark that provider as cooling down until `resetsAt`, and re-dispatch the task (resuming from the committed worktree state, not the session) to Codex, OpenCode or a local LLM. Because state lives in git, a different agent can pick up where the last one stopped.
- Avoid `--bare` for subscription-based Claude runs, since it requires an API key. Pin behaviour with `--settings` and `--allowedTools` instead.

### Gaps
- Exact licences for ccmanager, Crystal, Backlog.md and OpenHands could not be confirmed through the API in this session (marked "verify").
- No primary source checked for whether Nimbalyst (Crystal's successor) stays open source.
- No confirmed "Conductor" open-source equivalent. Conductor itself appears to be a closed macOS app (not verified here).
- Codex JSONL event names differ between doc mirrors, so verify them against the installed version.

## 2. .NET scheduling/queue options, wake, keep-awake, crash/reboot survival

### Takeaway
For a single-user desktop app, a **SQLite-backed task table plus an in-process `Channel<T>` worker** is the simplest robust design. Add **Windows Task Scheduler** (with "wake the computer to run this task") to launch OrchDesk at the start time, and **`SetThreadExecutionState(ES_CONTINUOUS | ES_SYSTEM_REQUIRED)`** while the queue runs. Quartz.NET (AdoJobStore on SQLite) is the strongest library if cron-like scheduling is needed. Hangfire is heavier and LGPL. Coravel has no persistence for scheduling.

### Cited Findings
- **Quartz.NET** AdoJobStore persists jobs and triggers via ADO.NET and supports providers `SQLite` and `SQLite-Microsoft`. You must create its tables from the shipped scripts. RAMJobStore loses everything on stop. Caveat: if you enlist your own transaction, SQLite locks in-process and a concurrent scheduler operation can fail with "database is locked". — [Quartz.NET job stores](https://www.quartz-scheduler.net/documentation/quartz-3.x/tutorial/job-stores.html). Licence Apache-2.0 (verify). Repo: https://github.com/quartznet/quartznet
- **Hangfire** Core is multi-licensed, with LGPLv3 as the open-source option. A paid subscription grants a commercial licence that "allows you to distribute private forks and modifications". — [Hangfire licenses](https://www.hangfire.io/licenses.html)
- **Hangfire.Storage.SQLite** (raisedapp): C#, about 178 stars, repo updated 2026-06-16. "An Alternative SQLite Storage for Hangfire". — [GitHub](https://github.com/raisedapp/Hangfire.Storage.SQLite)
- **SetThreadExecutionState**: `ES_CONTINUOUS | ES_SYSTEM_REQUIRED` keeps the system awake until it is cleared with `ES_CONTINUOUS`. Without `ES_CONTINUOUS` it only resets the idle timer, so it must be called periodically. It **cannot prevent user-initiated sleep (lid or power button)** and does not stop the screensaver. `ES_AWAYMODE_REQUIRED` is meant for media apps only. Disk and CPU activity are not detected automatically as "in use". — [Microsoft Learn](https://learn.microsoft.com/en-us/windows/win32/api/winbase/nf-winbase-setthreadexecutionstate)

### Inferences
- Persisted queue schema (SQLite via Microsoft.Data.Sqlite or EF Core): `tasks(id, repo_path, prompt, provider_pref, status{queued,running,verifying,done,failed,blocked}, attempt, max_attempts, branch, worktree_path, session_id, provider_used, started_at, ended_at, tokens_in/out, cost_usd, last_error)` plus an append-only `task_events` log. On startup, any `running` rows are crash orphans. Resume them by inspecting the worktree (`git status`/`git log`) and either re-dispatching with "continue from current branch state" or marking them failed.
- Use a single `Channel<TaskId>` (bounded) fed from the DB, with a `BackgroundService`-style worker in the WPF app via `Microsoft.Extensions.Hosting`. Concurrency is 1 for subscription CLIs (to share limits) and N for local LLMs.
- Wake/launch: `schtasks /Create /TN OrchDeskOvernight /TR "...OrchDesk.exe --overnight" /SC ONCE /ST 23:30` plus enabling "Wake the computer to run this task". The wake setting has no `schtasks` flag and needs XML (`<WakeToRun>true</WakeToRun>`) or the TaskScheduler COM API (the `TaskScheduler` NuGet by dahall is a common wrapper; verify). Also requires "Allow wake timers" in the power plan.
- Coravel is suited to in-memory scheduling and queues in ASP.NET-style hosts. It does not persist scheduled work (verify), so it is not ideal here.
- Reboot survival: Task Scheduler "At log on" or "At startup" trigger re-launches `OrchDesk.exe --resume-overnight`. Windows Update reboots are a known overnight risk. Consider setting Active Hours, or checking and deferring pending reboots.

### Gaps
- Did not fetch the Task Scheduler WakeToRun / schtasks docs or the Coravel and Quartz misfire/`RequestsRecovery` docs in this session. The points above are from prior knowledge and are marked as inference.
- Hangfire.Storage.SQLite licence was not confirmed.

## 3. Safety for unattended runs

### Takeaway
Combine CLI-level permission policy (allowlists and `dontAsk`/`--permission-prompts none`), per-task caps (turns, USD/tokens, wall clock, iterations), OS-level isolation (a worktree per task, optionally a Windows Sandbox, VM or container for risky repos), git guards (never touch main or force-push, and push only `orchdesk/*` branches), and a pre-push secret scan (gitleaks, optionally trufflehog). Add a global kill switch that sends SIGINT first, then kills the process tree.

### Cited Findings
- `--permission-mode dontAsk` denies every call that would otherwise prompt, which is useful for locked-down runs. `--permission-prompts none` makes Claude not retry denied actions. `--allowedTools` supports prefix rules such as `Bash(git diff *)`. Note the space before `*`. — [Claude Code headless docs](https://code.claude.com/docs/en/headless)
- A `-p` session without `--bare` runs the project's `.claude/settings.json` hooks and connects `.mcp.json` servers **even in untrusted folders, with no trust dialog**. This is a supply-chain risk when running agents on arbitrary repos. — [Claude Code headless docs](https://code.claude.com/docs/en/headless)
- Budget caps: `--max-budget-usd` (CLI) and `maxTurns`/`maxBudgetUsd` (SDK) with result subtypes `error_max_turns`/`error_max_budget_usd`. The budget is a client-side estimate and can overshoot. — [Agent SDK TS reference](https://code.claude.com/docs/en/agent-sdk/typescript)
- Kill semantics: SIGTERM gives exit 143, terminates running Bash process trees and runs SessionEnd hooks. SIGINT or SDK `interrupt()` ends the turn cleanly. — [Claude Code headless docs](https://code.claude.com/docs/en/headless)
- Codex: in non-interactive mode it does not ask for approvals. Use explicit `--sandbox workspace-write`. — [OpenAI Codex docs](https://developers.openai.com/codex/noninteractive.md)
- Ralph-style loops are often paired with `--dangerously-skip-permissions`. Practitioners advise sandboxed or disposable VMs and hard iteration caps. — [atcyrus.com](https://www.atcyrus.com/stories/ralph-wiggum-technique-claude-code-autonomous-loops)
- Secret scanners: `gitleaks/gitleaks` (Go, MIT, verify) and `trufflesecurity/trufflehog` (Go, **AGPL-3.0**, verify; it verifies live credentials). — https://github.com/gitleaks/gitleaks, https://github.com/trufflesecurity/trufflehog

### Inferences
- Policy in OrchDesk:
  - Agents never get `git push` permission. OrchDesk itself pushes after the checks pass.
  - Refuse to run if HEAD is `main`/`master`/the default branch.
  - Push with `git push origin orchdesk/<id>` only, never `--force`.
  - Optionally install a local `pre-push` hook that rejects default-branch refs.
- Run `gitleaks git --log-opts="<base>..HEAD"` (or `gitleaks protect --staged` per commit) as a subprocess. Block the push on any finding and flag it in the morning report. Bundle the gitleaks binary (MIT) rather than trufflehog (AGPL), unless trufflehog is only invoked as an external tool.
- Additional caps:
  - Wall-clock per task (for example 45 minutes, enforced through a Windows Job Object so the whole process tree dies).
  - Max diff size or files changed.
  - Deny network for local-LLM runs where possible.
  - Global "stop after N consecutive failures" breaker.
  - A kill-switch hotkey or tray item that also drops `ES_SYSTEM_REQUIRED`.
- Use `--settings` with an explicit deny list (for example `Bash(rm -rf *)`, `Bash(git push *)`, `Bash(curl *)`) rather than relying on defaults. The built-in starting mode can be `auto`, so always pass a mode explicitly.

### Gaps
- gitleaks and trufflehog licences and current activity were not verified through the API this session.
- Did not research Windows-specific sandboxing (Windows Sandbox `.wsb`, AppContainer, Dev Drive) in depth.

## 4. Morning report formats

### Takeaway
Generate one Markdown file per night (optionally rendered to HTML in the WPF app) with a summary table, then one section per task. Each section covers the outcome, the branch and PR link, `git diff --stat`, a truncated diff, test results, the provider/model actually used (with rotations), cost/tokens, limits hit and denials. All data is available from the headless JSON outputs plus git and the test runner.

### Cited Findings
- Claude JSON results provide `total_cost_usd`, per-model cost and `session_id`. On resumed sessions the totals include earlier runs, so take deltas. — [Claude Code headless docs](https://code.claude.com/docs/en/headless)
- Rate-limit telemetry: `rate_limit_event` (`status`, `resetsAt`, `utilization`) and `api_retry` events. Permission denials are listed in `permission_denials`. — [Agent SDK TS reference](https://code.claude.com/docs/en/agent-sdk/typescript); [headless docs](https://code.claude.com/docs/en/headless)
- Subscription limits use a 5-hour rolling window plus a weekly window, and `/usage` shows reset times. Resets can also happen ad hoc (a reported global reset on 2026-05-15, secondary source). — [DataCamp](https://www.datacamp.com/tutorial/claude-code-usage-limits); [pasqualepillitteri.it](https://pasqualepillitteri.it/en/news/2614/claude-resets-rate-limits-5-hour-weekly-may-15-2026)

### Inferences
Suggested template, `reports/overnight-YYYY-MM-DD.md` (the repo already has a `reports/` folder):
```
# OrchDesk overnight report - 2026-10-15 (23:30-07:05)
| # | Task | Status | Provider(s) | Attempts | Tests | Files/+/- | Cost/Tokens | Branch |
## Limits & rotations: claude-sub rejected 02:14 (resetsAt 04:30) -> codex; local-qwen used 3x
## Needs attention: failed / blocked / secret-scan hits / permission denials
## Task 1: <title>
- Outcome + agent's final summary (result field)
- Tests: dotnet test - 142 passed, 0 failed (trx link)
- Diff: git diff --stat main...orchdesk/2026-10-15/t1 (+ first ~200 lines, full patch file linked)
- Events: retries, denials, wall time
```
- Also emit a machine-readable `overnight-YYYY-MM-DD.json` for the UI. Use `dotnet test --logger trx` (or JUnit-style output for other stacks) to get structured test results.

### Gaps
- No standard "morning report" schema exists in the surveyed tools. The template above is a design proposal, not a sourced standard.

## 5. GitHub repositories available to this session's user (manniewarriner)

### Takeaway
`list_repos` returned 6 repos. Only **WebDev** (the current repo, which now contains the `OrchDesk.sln` skeleton) is clearly OrchDesk-related. **AlmondCRM** is the CRM repo. It is private and outside this session's GitHub scope, so its README could not be read. No repo named OrchDesk or orch-relay exists in the accessible list. Note that "Grok" may have been working on repos the session cannot see.

### Cited Findings
Source: `mcp__claude-code-remote__list_repos` (limit 200, `has_more:false`). The tool returns name, URL, visibility, `pushed_at` and `can_push` only. **It does not return description or language**, so those come from local inspection where available.

| Repo | Visibility | Last push (UTC) | Related to OrchDesk / agents / CRM? |
|---|---|---|---|
| [manniewarriner/WebDev](https://github.com/manniewarriner/WebDev) | public | 2026-10-14 20:40 | **Yes, this is the OrchDesk repo.** The local checkout has `OrchDesk.sln`, `src/OrchDesk.Core/{Accounts,Agent,Config,Models,Orchestration,Permissions,Providers,Tools}`, `tests/OrchDesk.Tests`, `reports/` and `research_notes/`. HEAD commit is "Wipe repository for new project" (earlier history was a cathedral/hero web page). Language: C# (local inspection). |
| [manniewarriner/AlmondCRM](https://github.com/manniewarriner/AlmondCRM) | private | 2026-10-13 19:04 | **Likely the CRM** the user mentioned. Possibly part of the "top level app" effort. Not readable in this session (access denied: only `manniewarriner/webdev` is in scope). |
| [manniewarriner/FixMyRide-Rblx](https://github.com/manniewarriner/FixMyRide-Rblx) | private | 2026-10-10 10:14 | Unlikely. Name suggests a Roblox game. |
| [manniewarriner/Metropolis-RP](https://github.com/manniewarriner/Metropolis-RP) | private | 2026-10-03 20:00 | Unlikely. Name suggests a roleplay game, probably Roblox. |
| [manniewarriner/linkpod](https://github.com/manniewarriner/linkpod) | private | 2026-10-01 08:45 | Unknown from the name alone. Possibly a link or podcast app. Not obviously agent-related. |
| [manniewarriner/Dean.BusinessAFW](https://github.com/manniewarriner/Dean.BusinessAFW) | private | 2026-09-21 22:22 | Unknown. "BusinessAFW" might be a business app framework, possibly CRM-adjacent. Could not verify. |

### Inferences
- If the "top level app / developer interface" work by Grok lives in AlmondCRM or Dean.BusinessAFW, the user would need to add those repos to the session (via `add_repo`) for inspection. I did not request this, per instructions.
- The existing `src/OrchDesk.Core/Orchestration` and `Permissions` folders are the natural homes for the overnight queue and the safety policy described above.

### Gaps
- Descriptions and languages for the five private repos are unavailable. `list_repos` does not return them, and the GitHub MCP is scoped to WebDev only.
- No repo named "orch-relay" was found among the accessible repos.
