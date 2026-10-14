# Open-source coding agents: designs to borrow and integrate for OrchDesk (C#/.NET 8 WPF), as of Oct 2026

Repo metadata below (licence, stars, last push, archived flag) comes from a single GitHub repository search run on 2026-10-14 — [GitHub search API](https://github.com/search?q=repo%3Aanomalyco%2Fopencode&type=repositories). Stars are rounded.

| Agent | Repo (canonical, Oct 2026) | Licence | Stars | Last push | Status |
|---|---|---|---|---|---|
| OpenCode | https://github.com/anomalyco/opencode (formerly sst/opencode) | MIT | ~213k | 2026-10-14 | very active |
| Claude Code | https://github.com/anthropics/claude-code | no SPDX licence on GitHub (proprietary/commercial terms) | ~151k | 2026-10-14 | very active |
| OpenAI Codex CLI | https://github.com/openai/codex | Apache-2.0 | ~129k | 2026-10-14 | very active |
| Gemini CLI | https://github.com/google-gemini/gemini-cli | Apache-2.0 | ~107k | 2026-10-14 | active; replaced by Antigravity CLI for unpaid/Google One users on 2026-06-18 |
| OpenHands | https://github.com/OpenHands/OpenHands (formerly All-Hands-AI) | MIT | ~91k | 2026-10-14 | active |
| Cline | https://github.com/cline/cline | Apache-2.0 | ~70k | 2026-10-14 | very active (now SDK + IDE + CLI) |
| Aider | https://github.com/Aider-AI/aider | Apache-2.0 | ~50k | 2026-05-22 | slowing (no push in ~5 months) |
| Continue | https://github.com/continuedev/continue | Apache-2.0 | ~36k | 2026-10-12 | active |
| Kilo Code | https://github.com/Kilo-Org/kilocode | MIT | ~28k | 2026-10-14 | active; Kilo acquired by Anaconda |
| Roo Code | https://github.com/RooCodeInc/Roo-Code | Apache-2.0 | ~24k | 2026-05-15 | **ARCHIVED** (shut down) |
| SWE-agent | https://github.com/SWE-agent/SWE-agent | MIT | ~21k | 2026-10-12 | active |
| Plandex | https://github.com/plandex-ai/plandex | MIT | ~16k | 2025-10-03 | dormant; cloud winding down |
| mini-swe-agent | https://github.com/SWE-agent/mini-swe-agent | MIT | ~8.5k | 2026-10-12 | active |
| Agent Client Protocol | https://github.com/agentclientprotocol/agent-client-protocol (formerly zed-industries) | Apache-2.0 | ~4.4k | 2026-10-14 | very active |
| goose | https://github.com/aaif-goose/goose (formerly block/goose) | Apache-2.0 per third parties (not verified) | n/a (repo not returned by search) | n/a | moved to the Linux Foundation AAIF in Apr 2026 |

## 1. Per-agent status and the design elements worth copying

### Takeaway
The live, well-funded projects (OpenCode, Codex, Claude Code, Cline, Gemini CLI, OpenHands, Kilo) have converged on the same design: a typed tool set, allow/ask/deny permission rules matched on tool input with globs, a separate plan mode, shadow-git or snapshot checkpoints that restore code and conversation separately, and a headless or server mode. Roo Code is archived and Plandex is dormant, so treat them only as sources of ideas, not as integration targets.

### Cited Findings
**OpenCode**
- The canonical repo is now anomalyco/opencode, under the MIT licence, with ~213k stars and a push on 2026-10-14 — [GitHub search](https://github.com/anomalyco/opencode)
- Permissions: each rule is `"allow" | "ask" | "deny"`, with a `"*"` catch-all. `bash`, `edit` and `webfetch` take glob objects matched on the tool's input, and the last matching rule wins. Example: `"bash": { "*": "ask", "git *": "allow", "rm *": "deny" }` — [OpenCode permissions](https://opencode.ai/docs/permissions/)
- Permission keys include `read` (`*.env` is denied by default), `edit` (covers edit, write and patch), `glob`, `grep`, `bash`, `task` (subagent launch), `skill`, `lsp`, `question`, `webfetch`, `websearch` and `external_directory` (asks by default for paths outside the project) — [OpenCode permissions](https://opencode.ai/docs/permissions/)
- **`doom_loop` guard**: it fires when the same tool call repeats 3 times with identical input, and asks by default. This is worth copying for weak local models — [OpenCode permissions](https://opencode.ai/docs/permissions/)
- Ask prompts offer `once`, `always` (approve the matching pattern for this session) or `reject`. `--auto` approves every "ask" but still honours explicit denies. Agents can override permissions per agent, and agents can be defined in Markdown with frontmatter — [OpenCode permissions](https://opencode.ai/docs/permissions/)
- `opencode serve` listens on 127.0.0.1:4096 and publishes an OpenAPI 3.1 spec at `/doc`. Endpoints: `POST /session` (optional `parentID`), `POST /session/:id/message` (waits for the reply), `POST /session/:id/prompt_async` (returns 204), `GET /event` (an SSE stream whose first event is `server.connected`), `GET /global/event`, and `POST /session/:id/permissions/:permissionID` with `response` and `remember`. Basic auth is enabled through `OPENCODE_SERVER_PASSWORD` — [OpenCode server](https://opencode.ai/docs/server/)
- `opencode acp` runs ACP over stdio JSON-RPC and supports tools, MCP, AGENTS.md, agents and permissions. `/undo` and `/redo` are not yet supported over ACP — [OpenCode ACP](https://opencode.ai/docs/acp/)

**Aider**
- Edit formats ([Aider edit formats](https://aider.chat/docs/more/edit-formats.html)):
  - `whole`: the model returns the full file. Simplest, but slow and costly.
  - `diff`: SEARCH/REPLACE blocks in git merge-conflict syntax.
  - `diff-fenced`: the file path goes inside the fence. Used for Gemini, which fails at plain `diff`.
  - `udiff`: a simplified unified diff. Used for GPT-4 Turbo to reduce lazy coding.
  - `editor-diff` / `editor-whole`: narrow prompts for the editor step of architect mode.
- The repo map lists files plus their key symbols, signatures and defining lines. Files are nodes in a dependency graph, and a graph-ranking algorithm picks the most relevant parts. `--map-tokens` defaults to 1k, and the map expands when no files are in the chat — [Aider repo map](https://aider.chat/docs/repomap.html)
- Architect/editor mode: one model reasons about the solution, and a second model turns it into well-formed edits ([Aider architect blog](https://aider.chat/2024/09/26/architect.html)):
  - o1-preview with a DeepSeek editor using `whole` scored 85.0%, against 79.7% for o1-preview solo.
  - Pairing a model with itself also helped: GPT-4o-mini went from 55.6% to 60.2%, and Sonnet from 77.4% to 80.5%.
- The last push to the repo was 2026-05-22 — [GitHub](https://github.com/Aider-AI/aider)

**OpenAI Codex CLI**
- `codex exec --json` writes JSONL to stdout ([Codex non-interactive](https://learn.chatgpt.com/docs/non-interactive-mode)):
  - Events: `thread.started`, `turn.started`, `turn.completed` (carries usage), `turn.failed`, `item.started` / `item.completed`, and `error`.
  - Item types: agent message, reasoning, command execution, file change, MCP tool call, web search and plan update.
  - Other flags: `--output-schema <file>` for a schema-conforming final answer, `codex exec resume --last` or `resume <SESSION_ID>`, `--ephemeral`, `--ignore-user-config` and `--skip-git-repo-check`.
- The sandbox defaults to read-only, with `--sandbox workspace-write` and `danger-full-access` as the other modes. `--full-auto` has been removed (use `--sandbox workspace-write`). `--approve-for-me` sends approvals through automatic review — [Codex non-interactive](https://learn.chatgpt.com/docs/non-interactive-mode)
- `codex app-server` speaks JSON-RPC 2.0 over stdio (newline-delimited; WebSocket is experimental) ([Codex app-server](https://learn.chatgpt.com/docs/app-server)):
  - Handshake: `initialize`, then `initialized`.
  - Methods: `thread/start`, `thread/resume`, `thread/fork`, `turn/start`, `turn/steer`, `turn/interrupt`.
  - Notifications: `item/started`, `item/completed`, `item/agentMessage/delta`, `turn/completed`, and others.
  - Approvals arrive as server→client requests such as `item/permissions/requestApproval` and `mcpServer/elicitation/request`, with `approvalPolicy` set on thread or turn start.
  - **Typed schemas:** `codex app-server generate-json-schema --out ./schemas` (and `generate-ts`). This is the recommended route for rich in-product clients.
- The approval policy (`untrusted`, `on-request` or `never`) is a separate axis from the sandbox. The sandbox uses Seatbelt on macOS and Landlock + seccomp on Linux, and the official guidance on Windows is to run in WSL or Docker — [OpenAI Codex sandboxing](https://developers.openai.com/codex/concepts/sandboxing); [Codex security](https://developers.openai.com/codex/security). Third-party blogs describe a native Windows sandbox built on restricted tokens, ACLs and firewall rules (unverified) — [danielvaughan blog](https://codex.danielvaughan.com/2026/07/18/codex-cli-windows-sandbox-architecture-powershell-ast-safety-elevated-unelevated-appcontainer-restricted-tokens/)

**Claude Code**
- `claude -p` supports `--output-format text|json|stream-json` and `--input-format stream-json`. Add `--include-partial-messages` (with `--verbose`) for token deltas — [Claude Code headless](https://code.claude.com/docs/en/headless)
  - The stream starts with `system/init` (model, tools, `mcp_servers`, `plugins`, and a `capabilities` array for feature detection) and ends with a `result` message (cost, session_id).
  - Retries show up as `system/api_retry` events.
  - Subagent messages carry `parent_tool_use_id`.
- `--json-schema` returns structured output in `structured_output`. `--resume <session_id>` (or the path to a `.jsonl` transcript) and `--continue` resume sessions. `--bare` is recommended for scripted and SDK calls and skips hooks, CLAUDE.md, MCP and plugins unless passed explicitly — [Claude Code headless](https://code.claude.com/docs/en/headless)
- Permissions: `--allowedTools` uses rule syntax such as `Bash(git diff *)`. `--permission-mode` takes `auto` (classifier), `dontAsk` or `acceptEdits`. `--permission-prompt-tool` delegates approvals to an MCP tool. `--permission-prompts none` denies anything that would prompt, and denials appear as `permission_denied` events and in the result's `permission_denials` list. SIGTERM exits with code 143 — [Claude Code headless](https://code.claude.com/docs/en/headless)
- The Agent SDK (Python/TS) exposes the same loop, with a `canUseTool` approval callback — [Claude Code headless](https://code.claude.com/docs/en/headless)
- Checkpoints ([Claude Code checkpointing](https://code.claude.com/docs/en/checkpointing)):
  - A checkpoint is taken before each user prompt that starts a turn, keeping the last 100 per session. Checkpoints persist with the session and are cleaned up after about 30 days.
  - `/rewind` offers "Restore code and conversation", "Restore conversation", "Restore code", "Summarize from here" and "Summarize up to here".
  - Limits: Bash-made file changes, most subagent edits, external edits and symlinks are not tracked or restored.

**Gemini CLI**
- Headless mode starts with `-p` or with no TTY. `--output-format json` gives `response`, `stats` and `error`. The streaming JSONL events are `init`, `message`, `tool_use`, `tool_result`, `error` and `result`. Exit codes: 0 success, 1 error, 42 input error, 53 turn limit — [Gemini CLI headless](https://geminicli.com/docs/cli/headless/)
- Checkpointing (off by default; enable with `general.checkpointing.enabled`):
  - Uses a shadow git repo at `~/.gemini/history/<project_hash>`, plus JSON conversation and pending-tool-call data in `~/.gemini/tmp/<project_hash>/checkpoints`.
  - A checkpoint is taken when a file-modifying tool is approved.
  - `/restore` reverts files and conversation, then **re-proposes the original tool call**.
  - Source: [Gemini CLI checkpointing](https://geminicli.com/docs/cli/checkpointing/)
- Gemini CLI was replaced by Antigravity CLI for unpaid and Google One users on 2026-06-18 — [Gemini CLI headless](https://geminicli.com/docs/cli/headless/)
- Gemini CLI is listed as a native ACP agent — [ACP agents](https://agentclientprotocol.com/overview/agents)

**Cline**
- Cline is now an SDK (`@cline/sdk`), a CLI (`npm i -g cline`, interactive or headless for CI), an ACP mode, a VS Code/JetBrains extension, a desktop app for open-weight models, and "Kanban" (`npx kanban`), which runs parallel agents in per-card worktrees with dependency chains — [Cline docs](https://docs.cline.bot/)
- Checkpoints ([Cline checkpoints](https://docs.cline.bot/core-workflows/checkpoints)):
  - A shadow git repo, separate from the project's own git, snapshots the project after **each tool use**, including untracked files.
  - Each checkpoint has a "Compare" diff and three restore options: Restore Files, Restore Task Only, and Restore Files & Task.
  - Checkpoints are framed as what makes auto-approve safe.

**Roo Code / Kilo Code**
- Roo Code's shutdown was announced on 2026-04-21, and the repo was archived on 2026-05-15 — [Kilo comparison page](https://kilo.ai/compare/roo-code-shutdown-roomote) (third-party/competitor source); archived flag confirmed by [GitHub](https://github.com/RooCodeInc/Roo-Code)
- Kilo Code (MIT) runs on VS Code, JetBrains and a CLI (`npm install -g @kilocode/cli`), has "modes", and uses the Kilo Gateway. The docs banner says "Kilo has been acquired by Anaconda" — [Kilo docs](https://kilo.ai/docs)

**OpenHands**
- The OpenHands Software Agent SDK is a redesign split into 4 packages (SDK, Tools, Workspace, Server) with typed, swappable components — [MLSys 2026 paper](https://proceedings.mlsys.org/paper_files/paper/2026/hash/8ae9cf363ea625161f885b798c1f1f78-Abstract-Conference.html); [arXiv 2511.03690](https://arxiv.org/html/2511.03690v1)
- The CLI offers a TUI, an ACP mode and `openhands --headless -t "task"` — [PyPI openhands](https://www.pypi.org/project/openhands/). The OpenHands-CLI repo says it is no longer actively maintained and points to "Agent Canvas", which conflicts with the PyPI page — [OpenHands-CLI](https://github.com/OpenHands/OpenHands-CLI)

**goose**
- goose moved from block/goose to github.com/aaif-goose/goose under the Linux Foundation's Agentic AI Foundation in April 2026 — [goose blog](https://goose-docs.ai/blog/2026/04/07/goose-moves-to-aaif)
- Permission modes are Auto, Approve, Smart Approve and Chat. Smart Approve asks only for tools the LLM classifies as write tools — [goose permissions](https://goose-docs.ai/docs/guides/goose-permissions)
- goose is listed as a native ACP agent — [ACP agents](https://agentclientprotocol.com/overview/agents)

**Plandex**
- The README says "Winding down as of 10/3/2025 and no longer accepting new users" (this refers to the cloud service). Other features: a diff review sandbox where changes stay separate until applied, tree-sitter project maps covering 30+ languages, and configurable autonomy levels — [Plandex README](https://raw.githubusercontent.com/plandex-ai/plandex/main/README.md)

**SWE-agent / mini-swe-agent**
- mini-swe-agent ([mini-swe-agent README](https://raw.githubusercontent.com/SWE-agent/mini-swe-agent/main/README.md)):
  - Bash is its only tool and it does not use tool calling. Each action is an independent `subprocess.run`, which makes it easy to sandbox through `docker exec`.
  - History is "completely linear", and the agent class is about 100 lines.
  - It claims ">74% on SWE-bench verified" (self-reported).
  - It reaches models through litellm or openrouter, and offers a Python API (`DefaultAgent(LitellmModel(...), LocalEnvironment())`).

### Inferences
- The patterns to copy into OrchDesk's native loop:
  1. OpenCode-style permission rules (allow/ask/deny with globs, last match wins, per-agent overrides), plus the `doom_loop` guard.
  2. A separate sandbox axis and approval axis, as in Codex.
  3. Shadow-git checkpoints taken after each mutating tool (Cline) or before each turn (Claude Code), with independent "restore code" and "restore conversation" actions.
  4. Gemini's "re-propose the pending tool call after restore".
  5. Aider-style ranked repo map with a token budget.
  6. Plandex-style staging of edits before applying them.
- In .NET, a shadow repo can be driven with LibGit2Sharp or with `git --git-dir=<shadow> --work-tree=<project>` (implementation suggestion; not from a source).
- Both Claude Code and Codex leave Bash-made file changes untracked by checkpoints, so OrchDesk checkpoints should snapshot the whole working tree (Cline's approach) rather than track individual edits.
- Avoid investing in Roo Code or Plandex integrations. Kilo's ownership changed, so watch it.

### Gaps
- I could not fetch the Continue CLI (`cn`) docs (404). Its headless and JSON details are unverified.
- I could not verify goose's licence or star count (the AAIF repo was not returned by search), or whether `goose acp` works in headless runs.
- I did not fetch first-party docs for SWE-agent's ACI (Agent-Computer Interface: a windowed file viewer, a linting-gated edit command, concise search outputs). These are known from the 2024 SWE-agent paper but are not cited here.
- I did not fetch first-party docs for Roo/Kilo modes and orchestrator ("boomerang" subtasks), Cline Plan/Act, Claude Code hooks (PreToolUse/PostToolUse etc.), or Codex's native Windows sandbox.
- No current SWE-bench leaderboard snapshot was collected.

## 2. Which edit format works best with weaker local models (Qwen/Gemma class)?

### Takeaway
Use search/replace blocks (Aider `diff` style, or a tool with `old_string`/`new_string`) as the default. Fall back to whole-file rewrites for small files, or when search/replace fails to apply. Avoid unified diff for small models. Splitting the work architect/editor style also helps weaker models.

### Cited Findings
- Diff-XYZ benchmark: the search-replace representation performs best for larger models across most tasks, with udiff variants slightly weaker. Smaller open models gain little from any format choice. Qwen2.5-Coder-7B scored very low on udiff-h generation (0.05 exact match) — [Diff-XYZ arXiv 2510.12487](https://arxiv.org/html/2510.12487v2) (search summary; full table not read)
- On the Aider benchmark with DeepSeek models, whole-file and search/replace performed comparably in both accuracy and token use, and unified diff performed worst — [Cascaded Code Editing arXiv 2604.19201](https://arxiv.org/pdf/2604.19201)
- Unified diff helped GPT-4 Turbo specifically, raising it from 26% to 59% by reducing laziness. This is model-specific evidence, not general evidence — [Aider unified diffs](https://aider.chat/docs/unified-diffs.html)
- Aider uses `diff-fenced` for Gemini-family models because they fail with the standard fence placement. This shows the format should be chosen per model family — [Aider edit formats](https://aider.chat/docs/more/edit-formats.html)
- Architect/editor pairing improved even small models: GPT-4o-mini rose from 55.6% to 60.2% when paired with itself, and `whole` was the most reliable editor format — [Aider architect](https://aider.chat/2024/09/26/architect.html)
- mini-swe-agent shows a bash-only agent with no tool calling can be competitive, which matters for local models with unreliable function calling — [mini-swe-agent](https://raw.githubusercontent.com/SWE-agent/mini-swe-agent/main/README.md)

### Inferences
- Recommended for OrchDesk's local-model loop:
  1. Expose an `edit` tool with `path`, `old_string`, `new_string` and an optional `replace_all` flag, requiring a unique match. Return a precise error with the closest match on failure.
  2. Expose a `write` (whole-file) tool for new or small files.
  3. Offer a text-protocol fallback (Aider SEARCH/REPLACE blocks parsed from plain text) for models whose JSON tool calls are flaky.
  4. Make the format selectable per model profile, as Aider does.
- An optional "architect = strong model, editor = local model" mode fits OrchDesk's delegation story. For example, Claude or Codex plans and a local Qwen applies the edits.

### Gaps
- I found no head-to-head published benchmark of edit formats on Qwen3 or Gemma 3 class models in 2026. Aider's leaderboard per-model "percent well-formed edits" column was not collected.

## 3. Most portable way to drive Claude Code / Codex / OpenCode / Gemini CLI from C#

### Takeaway
**ACP over stdio JSON-RPC is the one uniform protocol.** OpenCode, Gemini CLI, goose, Cline and OpenHands support it natively, and Claude Code and Codex support it through the `claude-agent-acp` and `codex-acp` adapters. So OrchDesk should implement one ACP client in C#. Keep native adapters for richer features: Claude `stream-json`, the Codex `app-server` (generated JSON Schema) or `exec --json`, and the `opencode serve` HTTP+SSE API.

### Cited Findings
- ACP adapter-based agents: Claude Agent (`claude-agent-acp`, zed-industries), Codex CLI (`codex-acp`, agentclientprotocol), Pi and Bub — [ACP agents](https://agentclientprotocol.com/overview/agents)
- Native ACP agents include Cline, Cursor, Factory Droid, Gemini CLI, GitHub Copilot (preview), Goose, Junie, Kimi CLI, Kiro CLI, Mistral Vibe, OpenCode, OpenHands, Qwen Code and others (39 listed) — [ACP agents](https://agentclientprotocol.com/overview/agents)
- The ACP repo is Apache-2.0, has ~4.4k stars, was pushed 2026-10-14, and now lives in the agentclientprotocol org — [GitHub](https://github.com/agentclientprotocol/agent-client-protocol)
- OpenCode: `opencode acp` speaks stdio JSON-RPC, with `/undo` and `/redo` not supported. `opencode serve` offers REST plus SSE with an OpenAPI 3.1 spec at `/doc`, so a C# client can be generated with NSwag or Kiota — [OpenCode ACP](https://opencode.ai/docs/acp/); [OpenCode server](https://opencode.ai/docs/server/)
- Codex: `codex exec --json` gives a one-shot JSONL event stream. `codex app-server` gives a bidirectional stdio JSON-RPC session with approvals as server requests, and `generate-json-schema` emits schemas pinned to the installed version — [Codex non-interactive](https://learn.chatgpt.com/docs/non-interactive-mode); [Codex app-server](https://learn.chatgpt.com/docs/app-server)
- Claude Code: `claude -p --output-format stream-json --input-format stream-json --verbose --include-partial-messages`, with approvals routed through `--permission-prompt-tool` (an MCP tool) or the SDK's `canUseTool`. `system/init` carries a `capabilities` array for feature detection, and SIGINT (not SIGTERM) cleanly ends a turn — [Claude Code headless](https://code.claude.com/docs/en/headless)
- Gemini CLI: `-p` with JSON or JSONL streaming (`init`, `message`, `tool_use`, `tool_result`, `error`, `result`), plus documented exit codes — [Gemini CLI headless](https://geminicli.com/docs/cli/headless/)

### Inferences
- A suggested C# architecture: an `IAgentBackend` abstraction with three implementations.
  1. `AcpBackend`: System.Diagnostics.Process with stdio, StreamJsonRpc or a hand-rolled newline JSON-RPC, and request handlers for ACP's permission and fs/terminal callbacks.
  2. `ClaudeStreamJsonBackend`.
  3. `CodexAppServerBackend`: generate C# types from the emitted JSON Schema.
  - Also an `OpenCodeHttpBackend` (HttpClient + SSE).
- ACP gives a uniform permission UI across all of them. The native modes add cost and usage data, structured output, resume and fork.
- OrchDesk could itself expose an ACP *agent* endpoint for its own local-model loop, so Zed and JetBrains could drive it too.

### Gaps
- I did not read the ACP spec method names themselves (`session/new`, `session/prompt`, `session/request_permission`, `fs/*`, `terminal/*`). These are from prior knowledge; verify them at https://agentclientprotocol.com.
- I found no official .NET ACP SDK; whether a C# SDK exists in the ACP org is unverified.

## 4. Which approval and checkpoint designs are worth copying?

### Takeaway
Copy the two-axis model (sandbox scope × approval policy) from Codex, glob rules with once/always/reject from OpenCode, a classifier-based "auto" mode from Claude Code or goose Smart Approve, and whole-tree shadow-git checkpoints with separate code and conversation restore from Cline, Claude Code and Gemini.

### Cited Findings
- Codex keeps the sandbox (read-only, workspace-write, full) separate from approval (untrusted, on-request, never) — [Codex sandboxing](https://developers.openai.com/codex/concepts/sandboxing)
- OpenCode: per-input glob rules where the last match wins; an `external_directory` ask for paths outside the project; `doom_loop`; and once/always/reject with session-scoped "always" — [OpenCode permissions](https://opencode.ai/docs/permissions/)
- Claude Code: rule syntax `Bash(git diff *)`; the modes `auto`, `dontAsk` and `acceptEdits`; and denials reported as structured events — [Claude Code headless](https://code.claude.com/docs/en/headless)
- goose Smart Approve asks only for write-classified tools — [goose permissions](https://goose-docs.ai/docs/guides/goose-permissions)
- Cline snapshots after every tool use (untracked files included) and offers three restore options — [Cline checkpoints](https://docs.cline.bot/core-workflows/checkpoints)
- Claude Code checkpoints before each turn, keeps 100, and offers restore of code and/or conversation plus "summarize from here". Bash edits are not tracked — [Claude Code checkpointing](https://code.claude.com/docs/en/checkpointing)
- Gemini CLI keeps a shadow repo in the user home and re-proposes the pending tool call after restore — [Gemini checkpointing](https://geminicli.com/docs/cli/checkpointing/)
- Plandex stages edits in a sandbox, separate from project files until applied — [Plandex README](https://raw.githubusercontent.com/plandex-ai/plandex/main/README.md)

### Inferences
- For Windows/WPF, Codex's OS sandbox isn't officially supported natively (WSL or Docker is recommended), so OrchDesk should rely on approvals plus checkpoints, with an optional WSL or Docker execution backend for shell commands, rather than on a native sandbox.
- When OrchDesk delegates to external CLIs, it should take its own shadow-git snapshot before and after each delegated run. External agents' checkpoints don't cover Bash changes, and ACP OpenCode lacks `/undo`.

### Gaps
- I did not verify Claude Code hook events or the OpenAI Codex execpolicy `.rules` format in detail.
