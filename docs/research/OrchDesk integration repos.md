# Drive the agent CLIs, borrow their designs

OrchDesk should embed only a small, permissively licensed set of .NET libraries and drive each coding agent through its documented headless contract. It should take the rest of its architecture from the leading open-source agents as design references rather than as code. Six findings shape that recommendation.

**Libraries to embed.** These are Microsoft.Extensions.AI with the official OpenAI and Anthropic C# SDKs, the official MCP C# SDK, and WPF-UI, AvalonEdit, DiffPlex.Wpf, Markdig/MdXaml and CommunityToolkit.Mvvm. All are MIT, Apache-2.0 or BSD and all target net8.0.

**One uniform protocol for external agents.** The Agent Client Protocol (ACP) is the single protocol that reaches OpenCode, Gemini CLI, Cline, goose and OpenHands natively, and Claude Code and Codex through adapters. It should sit beside richer native adapters: Claude `stream-json`, the Codex `app-server` and the `opencode serve` HTTP/SSE API.

**Routing stays in C#.** LLM gateways such as LiteLLM, Bifrost and Portkey only see HTTP traffic. They cannot fall back between subscription-authenticated CLIs, so OrchDesk has to own its router, with LiteLLM's cooldown semantics as the template.

**Overnight mode.** Every open-source parallel-agent manager (vibe-kanban, claude-squad, ccmanager, Crystal) uses the same pattern: one git worktree and branch per task, with a headless CLI running inside it. That pattern is worth re-implementing natively; none of these tools is a dependency worth taking.

**Biggest non-technical risks.** The first is Anthropic's subscription terms, which allow driving the unmodified `claude` binary but forbid touching its OAuth tokens. The second is .NET 8 reaching end of support on 2026-11-10, a month from now.

**The user's repos.** Of the user's six GitHub repos, only `manniewarriner/WebDev` is visibly OrchDesk. It contains the `OrchDesk.sln` skeleton that these recommendations map onto.

## Four agent CLIs already expose contracts OrchDesk can drive

The integration targets that matter most are the agents OrchDesk routes between, and each has a stable, machine-readable headless mode. **OpenCode** (now `anomalyco/opencode`, MIT, about 213k stars, pushed daily) offers the richest surface. `opencode serve` listens on `127.0.0.1:4096`, publishes an **OpenAPI 3.1 spec at `/doc`**, and exposes session CRUD, fork, revert, abort, synchronous and async prompts, an SSE event stream and a permission-reply endpoint, protected by `OPENCODE_SERVER_PASSWORD` basic auth ([OpenCode server](https://opencode.ai/docs/server/)). OrchDesk can therefore generate a typed C# client with NSwag or Kiota and keep one warm server instead of spawning a process per turn. `opencode run --attach http://localhost:4096` reuses that server from the CLI ([OpenCode CLI](https://opencode.ai/docs/cli/)).

**Codex CLI** (`openai/codex`, Apache-2.0, about 129k stars) provides `codex exec --json` for one-shot JSONL runs. It also provides `codex app-server`, a bidirectional stdio JSON-RPC 2.0 session with thread start/resume/fork, turn steer/interrupt, and approvals sent as server-to-client requests. Its `generate-json-schema` command **emits schemas pinned to the installed version**, which OrchDesk can turn into C# types ([Codex app-server](https://learn.chatgpt.com/docs/app-server); [Codex non-interactive](https://learn.chatgpt.com/docs/non-interactive-mode)).

**Claude Code** (`anthropics/claude-code`, which carries no SPDX licence and is proprietary) is driven with `claude -p --output-format stream-json --input-format stream-json --verbose --include-partial-messages`. Its `system/init` event carries a `capabilities` array for feature detection, and approvals can be delegated through `--permission-prompt-tool`. **SIGINT ends a turn cleanly, while SIGTERM exits with code 143** ([Claude Code headless](https://code.claude.com/docs/en/headless)).

ACP is what ties these together. Its repo (`agentclientprotocol/agent-client-protocol`, Apache-2.0, about 4.4k stars, very active) lists **39 native agents**, including OpenCode, Gemini CLI, Cline, goose, OpenHands and Qwen Code, plus the adapter agents `claude-agent-acp` and `codex-acp` ([ACP agents](https://agentclientprotocol.com/overview/agents)). One C# ACP client therefore buys a uniform permission UI across every backend, including ones OrchDesk does not target today. The trade-off is fidelity. Over ACP, OpenCode does not support `/undo` and `/redo` ([OpenCode ACP](https://opencode.ai/docs/acp/)), and the native modes expose cost, usage, structured output, resume and fork that ACP flattens. No official .NET ACP SDK was found, so OrchDesk would hand-roll newline-delimited JSON-RPC or use StreamJsonRpc. The research did not verify the ACP method names (`session/new`, `session/prompt`, `session/request_permission`), so check them against the spec before coding.

| Target | URL | Licence | Maturity (Oct 2026) | Integration mode | Key risks |
|---|---|---|---|---|---|
| OpenCode | https://github.com/anomalyco/opencode | MIT | ~213k stars, daily pushes | **Process + HTTP**: `opencode serve` with an OpenAPI-generated C# client; ACP as secondary | ACP lacks undo/redo; the `run --format json` schema is undocumented |
| Codex CLI | https://github.com/openai/codex | Apache-2.0 | ~129k stars, daily pushes | **Process**: `app-server` JSON-RPC with generated types; `exec --json` for one-shot runs | Event names drift between doc mirrors; native Windows sandbox is officially unsupported (WSL/Docker recommended) |
| Claude Code | https://github.com/anthropics/claude-code | Proprietary (no SPDX) | ~151k stars, daily pushes | **Process**: `stream-json` in both directions, spawning the user's own logged-in binary | ToS limits (see below); `rate_limit_event` schema is undocumented and changes between versions |
| Agent Client Protocol | https://github.com/agentclientprotocol/agent-client-protocol | Apache-2.0 | ~4.4k stars, very active | **Protocol**: one C# ACP client for all agents; optionally expose OrchDesk's own loop as an ACP agent | No .NET SDK; spec is still moving |
| Gemini CLI | https://github.com/google-gemini/gemini-cli | Apache-2.0 | ~107k stars, active | Optional ACP backend | Replaced by Antigravity CLI for unpaid/Google One users on 2026-06-18 ([Gemini CLI headless](https://geminicli.com/docs/cli/headless/)) |

## Routing and fallback belong inside OrchDesk, not a gateway

Half of OrchDesk's backends are CLIs authenticated by subscription, and no HTTP gateway can sit in front of `claude -p` or `codex exec`. A sidecar gateway would therefore duplicate logic for the HTTP half while leaving the hard half unsolved. The better approach is to **embed a small router in C#**: an ordered fallback list per role, a circuit breaker per backend, and error-class-to-action mapping, all built over `IChatClient` with Polly / Microsoft.Extensions.Http.Resilience. Microsoft.Extensions.AI ships no fallback client of its own, so this delegating `IChatClient` must be hand-written ([dotnet-guide](https://www.dotnet-guide.com/tutorials/dotnet-ai/provider-agnostic-chat-gateway/)).

The gateways are still worth studying for their semantics.

**LiteLLM** (`BerriAI/litellm`) separates general `fallbacks` from `context_window_fallbacks` and `content_policy_fallbacks`. Its documented cooldown is **3 allowed failures per minute, then 30 s cooldown**, and it runs background health checks every 300 s by default ([LiteLLM reliability](https://docs.litellm.ai/docs/proxy/reliability); [LiteLLM health](https://docs.litellm.ai/docs/proxy/health)). It is also a cautionary tale: **litellm 1.82.7 and 1.82.8 on PyPI were credential-stealing malware** on 24 March 2026 ([FutureSearch](https://futuresearch.ai/blog/litellm-pypi-supply-chain-attack/)).

**Bifrost** (`maximhq/bifrost`, Apache-2.0, Go, about 8.8k stars) is the best sidecar candidate if one is ever wanted. It has failover, budgets and an Ollama provider, but it ships via npx, Docker or a self-built Go binary ([GitHub](https://github.com/maximhq/bifrost)).

**Portkey gateway** (`Portkey-AI/gateway`, MIT, about 13.2k stars) is useful mainly for its config schema: fallback on chosen status codes and retries up to 5 with backoff ([GitHub](https://github.com/Portkey-AI/gateway)).

**RouteLLM** (`lm-sys/RouteLLM`, Apache-2.0, about 5.6k stars) has empty releases and dozens of open PRs, and its routers are trained on Arena chat rather than coding agents ([GitHub](https://github.com/lm-sys/RouteLLM)). For a single user, a transparent rule-based router that uses task type, token estimate and remaining quota beats a learned one.

Offline detection for the desktop PC needs no library. The design is to probe `GET {base}/v1/models` with about a 2 s connect timeout every 15–30 s, rather than LiteLLM's 300 s, because local machines change state quickly. Results classify as follows. A Cloudflare 502/530, connection refused, or an HTML Access login page means "offline". A 401/403 means an auth problem to surface, not a reason to fall back silently. Ollama and LM Studio do not authenticate by default, so neither should ever be exposed directly. The supported paths are a Cloudflare Tunnel with an Access **"Service Auth"** policy and `CF-Access-Client-Id`/`CF-Access-Client-Secret` headers, or Tailscale ([Ollama FAQ](https://docs.ollama.com/faq); [Cloudflare service tokens](https://developers.cloudflare.com/access/service-auth/service-token/); [LM Studio auth](https://beta.lmstudio.ai/docs/developer/core/authentication)).

OpenCode's paid APIs add one wrinkle for direct calls. **Zen** (`https://opencode.ai/inference/v1`) and **Go** ($10 or $40/month, `https://opencode.ai/inference/go/v1`) use different wire formats per model family. GPT and Grok models use `/responses`, Claude and some Qwen models use `/messages`, and only DeepSeek, GLM, Kimi, MiniMax, Mistral and some Qwen models use `/chat/completions` ([OpenCode Zen](https://opencode.ai/docs/zen/); [OpenCode Go](https://opencode.ai/docs/go/)). An OpenAI-compatible client alone therefore reaches only part of the catalogue, and OrchDesk should read `/models` at startup instead of hard-coding the list.

Claude subscription use carries the sharpest compliance edge. Anthropic's terms forbid third-party apps from offering Claude.ai login or from routing requests through plan credentials. They also forbid apps to **"collect, store, or intermediate Claude.ai credentials or session tokens"**. They do not prevent an end user from signing in to the unmodified Claude Code binary with their own subscription ([Claude Code legal](https://code.claude.com/docs/en/legal-and-compliance)). Server-side blocking of subscription OAuth in third-party clients began around January 2026 ([WinBuzzer](https://winbuzzer.com/2026/02/19/anthropic-bans-claude-subscription-oauth-in-third-party-apps-xcxwbn/)). Three design rules follow:

- OrchDesk spawns `claude` and never reads its credential files.
- OrchDesk does not use `--bare`, because bare mode ignores OAuth and requires `ANTHROPIC_API_KEY` ([Claude Code headless](https://code.claude.com/docs/en/headless)).
- Multiple `CLAUDE_CONFIG_DIR` profiles are for work/personal separation, not for automatically rotating around limits. Community tools such as claude-multiprofile automate the variable, which one source calls "stable but undocumented" ([claude-multiprofile](https://github.com/jmdarre-v/claude-multiprofile)).

Limit detection should watch `system/api_retry` with `error: "rate_limit"` and `rate_limit_event` with `status: "rejected"` or `errorCode: "credits_required"`, then cool the backend down until `resetsAt` ([Agent SDK TS reference](https://code.claude.com/docs/en/agent-sdk/typescript)).

## The .NET stack has converged on Microsoft.Extensions.AI

Every LLM library OrchDesk needs now plugs into the MEAI `IChatClient` abstraction, is stable, and targets net8.0 or netstandard2.0. That makes the "Providers" layer mostly assembly rather than invention.

**Main HTTP provider path.** The default should be `Microsoft.Extensions.AI` 10.11.0 plus `Microsoft.Extensions.AI.OpenAI`, over the official `OpenAI` 2.14.0 SDK with an `Endpoint` override. This one path covers Zen/Go chat-completions models, Ollama `/v1`, LM Studio and llama.cpp ([NuGet MEAI.OpenAI](https://www.nuget.org/packages/Microsoft.Extensions.AI.OpenAI); [openai-dotnet](https://github.com/openai/openai-dotnet)).

**Anthropic SDK and the package-name trap.** The official `Anthropic` package (12.57.0, MIT) ships `AsIChatClient()`. Its ID changed owners, though: **versions 3.x and below are the community tryAGI code**, and minor releases may contain breaking changes ([Claude C# SDK docs](https://platform.claude.com/docs/en/api/sdks/csharp)).

**Who runs the tool loop.** For a coding agent with approval gates, OrchDesk should run its own tool loop by reading `FunctionCallContent` from streamed updates, rather than using MEAI's automatic `UseFunctionInvocation()`.

**What to avoid or defer.** Semantic Kernel should be avoided for new code. Microsoft Agent Framework (`Microsoft.Agents.AI` 1.25.0) is its successor ([devblogs](https://devblogs.microsoft.com/semantic-kernel/semantic-kernel-and-microsoft-agent-framework/)), but it is heavy for a single-agent desktop app and should be adopted only if multi-agent workflows are needed.

| Library | URL | Licence | Version / maturity | Integration mode | Risks |
|---|---|---|---|---|---|
| Microsoft.Extensions.AI (+.OpenAI) | https://github.com/dotnet/extensions | MIT | 10.11.0, 2026-10-14 ([NuGet](https://api.nuget.org/v3-flatcontainer/microsoft.extensions.ai/index.json)) | **NuGet: core abstraction** | No fallback client; non-standard fields like `reasoning_content` are untested |
| OpenAI .NET SDK | https://github.com/openai/openai-dotnet | MIT | 2.14.0, 2026-09-15 | **NuGet: all OpenAI-compatible endpoints** | Only reaches chat-completions/responses models |
| Anthropic C# SDK | https://github.com/anthropics/anthropic-sdk-csharp | MIT | 12.57.0, 2026-10-14 | NuGet: optional, for API-key Claude and `/messages` models | Package ID changed owner; minor-version breaks |
| ModelContextProtocol | https://github.com/modelcontextprotocol/csharp-sdk | Apache-2.0 | 2.2.0, ~4.6k stars ([NuGet](https://api.nuget.org/v3/registration5-semver1/modelcontextprotocol/index.json)) | **NuGet: MCP client tools** become `AIFunction`s | None significant |
| OllamaSharp | https://github.com/awaescher/OllamaSharp | Probably MIT (not stated in NuGet metadata) | 5.5.0, 2026-09-30 ([NuGet](https://api.nuget.org/v3/registration5-semver1/ollamasharp/index.json)) | NuGet: optional, for model pull, `keep_alive` and `/api/ps` | Licence to confirm |
| Microsoft Agent Framework | https://github.com/microsoft/agent-framework | MIT | 1.25.0, GA April 2026 | Defer; design reference for multi-agent work | Heavy; 759 open issues |
| WPF-UI | https://github.com/lepoco/wpfui | MIT | 4.3.0, ~9.7k stars ([NuGet](https://api.nuget.org/v3/registration5-semver1/wpf-ui/index.json)) | **NuGet: Fluent shell and theming** | 459 open issues; 3.x→4.x changes not reviewed |
| CommunityToolkit.Mvvm | https://github.com/CommunityToolkit/dotnet | MIT | 8.4.2, Microsoft-maintained | **NuGet: MVVM and messenger** | None significant |
| AvalonEdit | https://github.com/icsharpcode/AvalonEdit | MIT | 6.3.1.120 (Apr 2025) ([NuGet](https://api.nuget.org/v3/registration5-semver1/avalonedit/index.json)) | **NuGet: code and tool-output viewer** | Slow release cadence |
| DiffPlex / DiffPlex.Wpf | https://github.com/mmanela/diffplex | Apache-2.0 | 1.9.x, ~1.3k stars ([NuGet](https://api.nuget.org/v3/registration5-semver1/diffplex.wpf/index.json)) | **NuGet: diff review before approval** | None significant |
| Markdig + MdXaml | https://github.com/xoofx/markdig, https://github.com/whistyun/MdXaml | BSD-2 / MIT | Markdig 1.4.0; MdXaml 1.27.0 stable ([NuGet](https://api.nuget.org/v3-flatcontainer/mdxaml/index.json)) | NuGet: streamed markdown into a FlowDocument | Re-rendering per token is slow; avoid the stale Markdig.Wpf (last release 2021) |
| H.NotifyIcon.Wpf | https://github.com/HavenDV/H.NotifyIcon | MIT | **Pin 2.3.2 on net8**; 2.4.1 is net10-only ([NuGet](https://api.nuget.org/v3-flatcontainer/h.notifyicon.wpf/index.json)) | NuGet: tray icon for overnight mode | TFM trap on upgrade |
| WebView2 | (Microsoft NuGet) | Microsoft licence file | 1.0.4258.31 stable | Optional rich renderer only | Runtime may be missing or frozen on locked-down laptops ([MS Learn](https://learn.microsoft.com/en-us/microsoft-edge/webview2/concepts/distribution)) |

**Single-file publishing.** All the managed packages bundle into a self-contained single-file exe. Native DLLs need `IncludeNativeLibrariesForSelfExtract=true`, which extracts them to `%TEMP%\.net`, and **`PublishTrimmed` must stay off, because the SDK disables trimming for WPF** ([single-file overview](https://learn.microsoft.com/en-us/dotnet/core/deploying/single-file/overview); [trimming incompatibilities](https://learn.microsoft.com/lt-lt/dotnet/core/deploying/trimming/incompatibilities)).

**Toast notifications.** No option is clean yet. `Microsoft.Toolkit.Uwp.Notifications` has been frozen since 2022, and its repo is archived. Windows App SDK `AppNotificationManager` is Microsoft's recommended path, but sources conflict on whether it works unpackaged ([MS Learn notifications](https://learn.microsoft.com/et-ee/windows/apps/develop/notifications/); [CommunityToolkit docs PR](https://github.com/MicrosoftDocs/CommunityToolkit/pull/655/files)). Use H.NotifyIcon balloon tips until a prototype settles the question.

**Retargeting.** Because **.NET 8 support ends 2026-11-10** ([devblogs](https://devblogs.microsoft.com/dotnet/dotnet-8-9-end-of-support/)), OrchDesk should plan a move to net10.0-windows now. Every library above already ships net10 assets.

## Copy permissions, checkpoints and edit formats from the agents

OrchDesk's own local-model loop should be original C#, but its behaviour should copy what the large agents have converged on.

**Permissions.** OpenCode's model is the most transferable: allow/ask/deny rules matched on tool input with globs, where the **last matching rule wins**. It includes an `external_directory` ask for paths outside the project and once/always/reject replies. Its **`doom_loop` guard trips when the same tool call repeats 3 times with identical input**, which is valuable for weak local models ([OpenCode permissions](https://opencode.ai/docs/permissions/)). Codex keeps sandbox scope (read-only, workspace-write, full) and approval policy (untrusted, on-request, never) as **two independent axes**, and OrchDesk should mirror that split ([Codex sandboxing](https://developers.openai.com/codex/concepts/sandboxing)). goose's Smart Approve, which asks only for write-classified tools, is a good model for a middle setting ([goose permissions](https://goose-docs.ai/docs/guides/goose-permissions)).

**Checkpoints.** Cline snapshots the project into a **shadow git repo after every tool use, including untracked files**. It offers Restore Files, Restore Task Only, or both ([Cline checkpoints](https://docs.cline.bot/core-workflows/checkpoints)). Claude Code checkpoints before each turn and keeps 100, but it **does not track Bash-made changes** ([Claude Code checkpointing](https://code.claude.com/docs/en/checkpointing)). Gemini CLI re-proposes the pending tool call after a restore ([Gemini checkpointing](https://geminicli.com/docs/cli/checkpointing/)). The implication is that OrchDesk should snapshot the whole working tree itself, before and after every delegated CLI run, because the external agents' own checkpoints miss shell edits.

**Edit formats for local Qwen/Gemma-class models.** The evidence favours search/replace edits. On Aider's benchmark with DeepSeek models, whole-file and search/replace performed comparably and **unified diff performed worst** ([arXiv 2604.19201](https://arxiv.org/pdf/2604.19201)). Qwen2.5-Coder-7B scored 0.05 exact match on one udiff variant in Diff-XYZ ([arXiv 2510.12487](https://arxiv.org/html/2510.12487v2)). Aider chooses the format per model family, for example `diff-fenced` for Gemini ([Aider edit formats](https://aider.chat/docs/more/edit-formats.html)). Its architect/editor split raised GPT-4o-mini from **55.6% to 60.2%** even when the model was paired with itself ([Aider architect](https://aider.chat/2024/09/26/architect.html)). That maps directly onto OrchDesk's routing: Claude or Codex plans, and a local model applies the edits. mini-swe-agent (`SWE-agent/mini-swe-agent`, MIT) shows that a bash-only agent with no function calling can still be competitive. It claims more than 74% on SWE-bench Verified, a self-reported figure ([README](https://raw.githubusercontent.com/SWE-agent/mini-swe-agent/main/README.md)). It is the right fallback design when a local model's JSON tool calls are unreliable.

| Reference | URL | Licence | Maturity | What to borrow | Risks |
|---|---|---|---|---|---|
| OpenCode | https://github.com/anomalyco/opencode | MIT | Very active | Permission DSL, `doom_loop`, Markdown-defined agents | None |
| Codex CLI | https://github.com/openai/codex | Apache-2.0 | Very active | Sandbox × approval axes | Windows sandbox is not first-class |
| Cline | https://github.com/cline/cline | Apache-2.0 | Very active (~70k stars) | Per-tool shadow-git checkpoints; Kanban worktree runs ([Cline docs](https://docs.cline.bot/)) | None |
| Aider | https://github.com/Aider-AI/aider | Apache-2.0 | **Slowing**: last push 2026-05-22 | Edit formats, architect/editor, ranked repo map ([repo map](https://aider.chat/docs/repomap.html)) | Reference only |
| mini-swe-agent / SWE-agent | https://github.com/SWE-agent/mini-swe-agent | MIT | Active | Bash-only loop for weak tool-callers | Python |
| goose | https://github.com/aaif-goose/goose | Apache-2.0 per third parties, unverified | Moved to the Linux Foundation's AAIF in Apr 2026 ([goose blog](https://goose-docs.ai/blog/2026/04/07/goose-moves-to-aaif)) | Smart Approve | Licence and activity unverified |
| OpenHands | https://github.com/OpenHands/OpenHands | MIT (core) | ~91k stars | Typed SDK split (SDK/Tools/Workspace/Server) ([arXiv](https://arxiv.org/html/2511.03690v1)); optional sandboxed backend | Heavy Python/Docker; CLI maintenance signals conflict |
| Plandex | https://github.com/plandex-ai/plandex | MIT | **Dormant**; cloud winding down ([README](https://raw.githubusercontent.com/plandex-ai/plandex/main/README.md)) | Staging edits before applying them | Do not integrate |
| Roo Code | https://github.com/RooCodeInc/Roo-Code | Apache-2.0 | **Archived 2026-05-15** | Ideas only | Dead project |
| Kilo Code | https://github.com/Kilo-Org/kilocode | MIT | Active; acquired by Anaconda ([Kilo docs](https://kilo.ai/docs)) | Modes | Ownership change |

## Overnight runs need a worktree per task and a persisted queue

The parallel-agent managers are all useful, and none of them is embeddable. **vibe-kanban** (`BloopAI/vibe-kanban`, Rust, **Apache-2.0**, about 28.4k stars) is the best source to borrow from. Its task state machine and per-agent executor abstraction translate cleanly to C# ([GitHub](https://github.com/BloopAI/vibe-kanban)). **claude-squad** (`smtg-ai/claude-squad`, **AGPL-3.0**, about 8.6k stars) depends on tmux, so it is a reference only ([GitHub](https://github.com/smtg-ai/claude-squad)). **ccmanager** (`kbwo/ccmanager`, MIT unverified, about 1.26k stars) covers the widest range of agents ([GitHub](https://github.com/kbwo/ccmanager)). **Crystal** has rebranded to Nimbalyst, and its open-source future is unclear ([GitHub](https://github.com/stravu/crystal)).

**Task file formats.** **Backlog.md** (`MrLesk/Backlog.md`, about 7.0k stars, active, MIT unverified) stores tasks as markdown files in the repo. That makes it a natural human-editable "queue it before bed" format ([GitHub](https://github.com/MrLesk/Backlog.md)). **Taskmaster** (`eyaltoledano/claude-task-master`, about 28.2k stars) has a NOASSERTION/Commons Clause licence and no push since April 2026. Read its format but do not depend on it ([GitHub](https://github.com/eyaltoledano/claude-task-master)).

**Per-task loop.** The core loop has seven steps:

1. Add a worktree on an `orchdesk/<date>/<id>` branch.
2. Run the chosen CLI headless and persist its `session_id`.
3. Run the tests.
4. Re-prompt on failure, up to a hard cap. This is a bounded "Ralph" loop; practitioners advise iteration caps of 20–50 and sandboxing ([atcyrus.com](https://www.atcyrus.com/stories/ralph-wiggum-technique-claude-code-autonomous-loops)).
5. Commit.
6. Secret-scan.
7. Push only the task branch.

Because state lives in git rather than in the session, a task that hits a Claude limit can be re-dispatched to Codex, OpenCode or the local model from the same branch.

**Scheduling and keep-awake.** For a single-user desktop app, a SQLite task table feeding an in-process `Channel<T>` worker is enough. **Quartz.NET** (`quartznet/quartznet`, Apache-2.0 unverified) is the library to reach for if cron-style scheduling is needed. Its AdoJobStore supports SQLite, but it can hit "database is locked" when it shares transactions ([Quartz.NET job stores](https://www.quartz-scheduler.net/documentation/quartz-3.x/tutorial/job-stores.html)). Hangfire's open-source option is LGPLv3 and heavier ([Hangfire licenses](https://www.hangfire.io/licenses.html)). Windows Task Scheduler with WakeToRun launches the app. `SetThreadExecutionState(ES_CONTINUOUS | ES_SYSTEM_REQUIRED)` keeps the machine awake, but it **cannot block lid-close or power-button sleep** ([MS Learn](https://learn.microsoft.com/en-us/windows/win32/api/winbase/nf-winbase-setthreadexecutionstate)).

**Unattended safety.** Pass explicit `--permission-mode dontAsk` with allowlists and `--max-budget-usd` (a client-side estimate that can overshoot). Remember that a `-p` run without `--bare` **executes the repo's `.claude/settings.json` hooks and `.mcp.json` servers with no trust dialog** ([Claude Code headless](https://code.claude.com/docs/en/headless)). That is a supply-chain risk when running agents across arbitrary repos. For secret scanning, bundle **gitleaks** (`gitleaks/gitleaks`, MIT unverified) as a pre-push gate. Call **trufflehog** (AGPL-3.0, unverified) only as an external tool, if at all.

## The user's repos: WebDev is OrchDesk, the rest are out of scope

The session could see six repos under `manniewarriner`. **[WebDev](https://github.com/manniewarriner/WebDev)** (public, pushed 2026-10-14) is the OrchDesk repo. Its HEAD commit wiped an earlier web page, and it now holds `OrchDesk.sln` with `src/OrchDesk.Core/{Accounts, Agent, Config, Models, Orchestration, Permissions, Providers, Tools}` and `tests/OrchDesk.Tests`. That layout maps neatly onto this report:

| Folder | What goes there |
|---|---|
| `Providers` | The MEAI clients and the C# router |
| `Agent` | The CLI and ACP backends |
| `Permissions` | The OpenCode-style rules and git guards |
| `Orchestration` | The worktree queue |
| `Accounts` | `CLAUDE_CONFIG_DIR` profiles and secrets in DPAPI or Credential Manager |

**[AlmondCRM](https://github.com/manniewarriner/AlmondCRM)** (private, pushed 2026-10-13) is most likely the user's CRM. It was outside this session's GitHub scope, so its contents are unknown. **[Dean.BusinessAFW](https://github.com/manniewarriner/Dean.BusinessAFW)** has a name that may mean a business app framework, which is unverified. **FixMyRide-Rblx**, **Metropolis-RP** and **linkpod** look unrelated (two Roblox-style games and a link app). No repo named OrchDesk or orch-relay exists in the accessible list. If earlier "top level app" work lives in AlmondCRM or Dean.BusinessAFW, those repos need to be added to a session before they can be assessed for reuse. None of the user's repos is currently a candidate dependency.

## Prioritized shortlist for OrchDesk

| Priority | Item | Repo | Licence | Mode | Why now |
|---|---|---|---|---|---|
| P0 | Microsoft.Extensions.AI + OpenAI SDK | dotnet/extensions, openai/openai-dotnet | MIT | NuGet | One provider path for Zen/Go chat, Ollama, LM Studio and llama.cpp; foundation for the router |
| P0 | Claude Code `stream-json` adapter | anthropics/claude-code | Proprietary | Spawn the user's binary | Primary subscription backend; limit events drive fallback; ToS-compliant only this way |
| P0 | Codex `app-server` adapter | openai/codex | Apache-2.0 | Spawn + JSON-RPC with generated types | Second subscription backend with typed approvals |
| P0 | `opencode serve` client | anomalyco/opencode | MIT | HTTP/SSE via an OpenAPI-generated client | Warm agent loop over Zen/Go and local models |
| P0 | WPF-UI, CommunityToolkit.Mvvm, AvalonEdit, DiffPlex.Wpf, Markdig + MdXaml | as listed above | MIT / Apache / BSD | NuGet | The whole chat, diff and approval surface, all single-file-safe |
| P0 | Retarget to net10.0-windows | — | — | Build | .NET 8 support ends 2026-11-10 |
| P1 | OpenCode permission DSL + Codex two-axis model + Cline shadow-git checkpoints | opencode, codex, cline | MIT / Apache | Design reference | Safe auto-approve for the native loop and for delegated runs |
| P1 | Worktree-per-task queue (vibe-kanban design) + SQLite + Task Scheduler + gitleaks | BloopAI/vibe-kanban, gitleaks/gitleaks | Apache-2.0 / MIT | Design reference + bundled binary | Overnight mode with provider rotation and a pre-push gate |
| P1 | LiteLLM fallback/cooldown semantics | BerriAI/litellm | MIT core | Design reference only | Cooldown numbers and error classes to copy into C# |
| P1 | ModelContextProtocol C# SDK | modelcontextprotocol/csharp-sdk | Apache-2.0 | NuGet | MCP tools in the local-model loop |
| P2 | ACP client (and optionally an ACP agent endpoint) | agentclientprotocol/agent-client-protocol | Apache-2.0 | Protocol | Unlocks Gemini, goose, Cline, OpenHands and Qwen Code at once |
| P2 | Aider edit formats + architect/editor; mini-swe-agent bash fallback | Aider-AI/aider, SWE-agent/mini-swe-agent | Apache-2.0 / MIT | Design reference | Makes weak local models useful |
| P2 | Anthropic C# SDK, OllamaSharp | anthropics/anthropic-sdk-csharp, awaescher/OllamaSharp | MIT | NuGet | `/messages` models on Zen and Ollama-native warm-up |
| P2 | Backlog.md task format | MrLesk/Backlog.md | MIT (unverified) | File-format interop | Human-editable overnight queue |
| Avoid | Gateways as sidecars, Semantic Kernel, Roo Code, Plandex, Taskmaster, claude-squad code, trufflehog embedding, Markdig.Wpf | — | — | — | Duplicate logic, dead projects, or licence and supply-chain risk |

## Conclusion

The ecosystem has done OrchDesk an unusual favour. Every agent it routes between now publishes a machine-readable control surface: an OpenAPI spec, a generated JSON Schema, NDJSON with capability flags, and ACP over all of them. The defensible part of OrchDesk is therefore not its integrations. It is the policy layer above them, made of the router, the cooldown logic, the permission rules, the whole-tree checkpoints and the git-anchored task state that lets work move between providers mid-task. That is the layer no open-source project offers for Windows, and it is the one to spend engineering time on.

Two things are less settled than they look. First, the cheapest compliant path to Claude is also the most fragile: limit-event schemas are undocumented and change between versions, and Anthropic enforces its rules "without prior notice". OrchDesk should log raw events per CLI version and treat Claude as one backend that can be revoked, never as the default it cannot run without. Second, several licences (ccmanager, Backlog.md, gitleaks, OllamaSharp, goose, Quartz.NET) and the ACP method names were not verified in this research. Each needs a quick check before it moves from "design reference" to "shipped in the exe".
