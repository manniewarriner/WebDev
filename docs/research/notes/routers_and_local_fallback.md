# Routers, Gateways and Local-Model Fallback for OrchDesk (as of Oct 2026)

Context: OrchDesk is a single-exe C#/.NET 8 WPF agent that routes between OpenCode (Zen/Go API and the `opencode` CLI), the Claude Code CLI (subscription), the Codex CLI, and LLMs on the user's desktop PC (Ollama / LM Studio / llama.cpp / vLLM), reached through a Cloudflare Worker + Access service token or Tailscale, with automatic cloud fallback.

Method note: the GitHub API was not reachable from this session, so repo licences and star counts come from the GitHub web pages, and exact "last commit" dates could not be read. Treat activity claims as approximate.

## Q1. Embed routing in C# or delegate to a gateway (LiteLLM, Bifrost, Portkey, OpenRouter, RouteLLM, NotDiamond/Martian, .NET-native)?

### Takeaway
Build the router into OrchDesk in C#. Half of OrchDesk's backends are CLIs (`claude -p`, `codex`, `opencode run`) that no HTTP gateway can front. The HTTP half (OpenCode Zen/Go and local OpenAI-compatible servers) needs only a small "ordered fallback + cooldown + health probe" layer, which is easy to write with Polly / Microsoft.Extensions.Http.Resilience over Microsoft.Extensions.AI `IChatClient`.

Use LiteLLM and Bifrost as design references: copy their `fallbacks`, `allowed_fails`, `cooldown_time` and background health-check semantics. If a sidecar is ever wanted, Bifrost (a single Go service, Apache-2.0) fits a desktop app better than LiteLLM (a Python service with a 2026 supply-chain incident).

### Cited Findings

**LiteLLM (BerriAI/litellm)**
- Fallbacks come in three types: `fallbacks` (general errors such as rate limits), `context_window_fallbacks` and `content_policy_fallbacks`. There is also `default_fallbacks`, and fallbacks can be set per request — [LiteLLM reliability docs](https://docs.litellm.ai/docs/proxy/reliability)
- `num_retries` sets retries before fallback, and `request_timeout` sets the timeout. The cooldown example uses `allowed_fails` = 3 failures per minute and `cooldown_time` = 30 s. Fallback targets are checked against budget and access, and spend logs record `attempted_fallbacks` — [LiteLLM reliability docs](https://docs.litellm.ai/docs/proxy/reliability)
- Health endpoints:
  - `/health/liveliness` takes no auth and checks no dependencies.
  - `/health/readiness` takes no auth.
  - `/health` makes a real test call per model, which costs tokens.
  - `background_health_checks: true` with `health_check_interval` (default 300 s) caches the results.
  - Routing away from unhealthy deployments is covered on a separate "Health Check Driven Routing" page.
  - [LiteLLM health docs](https://docs.litellm.ai/docs/proxy/health)
- Supply-chain incident: litellm 1.82.7 and 1.82.8 on PyPI (24 March 2026) were malicious credential stealers. 1.82.8 added a `.pth` launcher that ran on any Python start. Both were yanked the same day. The attribution to "TeamPCP" comes from a single source — [FutureSearch](https://futuresearch.ai/blog/litellm-pypi-supply-chain-attack/), [pypistats blog](https://pypistats.com/blog/litellm-pypi-supply-chain-compromise-how-a-popular-llm-proxy-became-a-credential-stealing-backdoor-march-24-2026), [Bastion](https://bastion.tech/blog/litellm-pypi-supply-chain-attack)

**Bifrost (maximhq/bifrost)**
- Apache-2.0, written in Go, about 8.8k stars and more than 8,194 commits on `dev`.
- Features: provider/model failover, load balancing across keys and providers, virtual keys with hierarchical budgets, an OpenAI-compatible `/v1/chat/completions`, an Ollama provider, semantic caching, an MCP gateway and a web UI.
- Deployment: `npx -y @maximhq/bifrost`, Docker, or the Go SDK. The repo page does not mention a standalone binary download or health checks.
- [GitHub maximhq/bifrost](https://github.com/maximhq/bifrost)

**Portkey gateway (Portkey-AI/gateway)**
- MIT licence, about 13.2k stars, TypeScript/Node.
- Features: fallbacks on chosen error codes, up to 5 retries with exponential backoff, weighted load balancing and conditional routing.
- Runs with `npx @portkey-ai/gateway` on `localhost:8787/v1`.
- A "Gateway 2.0" pre-release on the `2.0.0` branch merges the enterprise gateway into open source.
- [GitHub Portkey-AI/gateway](https://github.com/Portkey-AI/gateway)

**RouteLLM (lm-sys/RouteLLM)**
- Apache-2.0, about 5.6k stars, 175 commits.
- Routers: `mf` (recommended), `sw_ranking`, `bert`, `causal_llm` and `random`.
- Includes an OpenAI-compatible server (`python -m routellm.openai_server`, with model name `router-mf-<threshold>`) and threshold calibration against Arena data.
- Releases are empty. There are 29 open issues and 16 open PRs, so maintenance status is unclear.
- [GitHub lm-sys/RouteLLM](https://github.com/lm-sys/RouteLLM)

**.NET options**
- Microsoft.Extensions.AI (MEAI) has no built-in fallback client. The usual pattern is a custom delegating `IChatClient`.
- MEAI's own Ollama adapter is deprecated in favour of OllamaSharp, whose `OllamaApiClient` implements `IChatClient`.
- A third-party NuGet package, BYOK.Core, advertises per-model fallback chains, including `ollama:` and generic OpenAI-compatible routes. It has not been vetted.
- [dotnet-guide](https://www.dotnet-guide.com/tutorials/dotnet-ai/provider-agnostic-chat-gateway/), [OllamaSharp NuGet](https://packages.nuget.org/packages/OllamaSharp), [BYOK.Core NuGet](https://www.nuget.org/packages/BYOK.Core)

### Integration-mode summary

| Candidate | URL | Licence | Activity (approx.) | Mode for OrchDesk | Risks |
|---|---|---|---|---|---|
| LiteLLM | github.com/BerriAI/litellm | MIT core plus a separately licensed `enterprise/` directory (from prior knowledge; not re-verified) | Very active | Design reference (optional HTTP sidecar) | Python runtime and Postgres for budgets; heavy for a single exe; the March 2026 PyPI compromise means versions must be pinned and hashed |
| Bifrost | github.com/maximhq/bifrost | Apache-2.0 | Very active (8k+ commits) | Best sidecar candidate (Docker or npx; Go build) / design reference | Needs Node (npx) or Docker unless you build the Go binary yourself; no health-check docs on the README |
| Portkey gateway | github.com/Portkey-AI/gateway | MIT | Active; 2.0 in pre-release | Design reference for its config schema (fallback on status codes, retry) | Node runtime; 2.0 changes are in flux |
| RouteLLM | github.com/lm-sys/RouteLLM | Apache-2.0 | Unclear / low | Design reference only (cost-quality threshold idea) | Python and torch models; trained on Arena chat, not on coding agents |
| OpenRouter | openrouter.ai (hosted) | Proprietary service | — | Optional extra cloud backend (OpenAI-compatible) | Not researched this session (see Gaps) |
| MEAI + Polly / Microsoft.Extensions.Http.Resilience | NuGet | MIT | Active (MEAI 10.x) | **Recommended: embed** | Fallback middleware must be hand-written |
| OllamaSharp | NuGet | MIT (from prior knowledge) | Active (5.4.x) | NuGet | — |

### Inferences
- Gateways only see HTTP traffic. Claude Code and Codex are subscription-authenticated CLIs, so a gateway cannot fallback across them. OrchDesk needs its own C# router anyway, so a sidecar would only duplicate logic for the HTTP backends.
- Recommended C# design, modelled on LiteLLM:
  - An ordered `fallbacks` list per "role" (planner, coder, cheap).
  - A per-backend circuit breaker: open after N failures within 60 s (LiteLLM uses `allowed_fails` = 3), then cool down for 30–60 s.
  - Map error classes to actions: 429 or limit means fallback with a long cooldown until the reset time; connection refused or timeout means mark offline; context exceeded means use the context-window fallback.
  - A background probe every 15–30 s for local endpoints, rather than every 300 s, because local machines change state quickly.
- RouteLLM-style learned routing is overkill. A rule-based router using task type, token estimate and remaining quota is more transparent for a single user.

### Gaps
- NotDiamond and Martian were not researched in this session. They are hosted, proprietary routers; check their current status before considering them.
- No .NET-native, YARP-based LLM gateway with fallback semantics was found. YARP itself was not reviewed.
- OpenRouter fallback (`models` array) and provider-routing specifics were not fetched this session.
- Exact latest-commit dates and releases could not be retrieved (GitHub API blocked).

## Q2. OpenCode Zen/Go API and the `opencode` headless server

### Takeaway
Zen is a pay-as-you-go multi-format API at `https://opencode.ai/inference/v1`. Go is a $10 or $40 per month subscription at `https://opencode.ai/inference/go/v1`. Both use Bearer API keys and expose `/models`. Only some models use OpenAI `/chat/completions`; others need `/responses`, `/messages` (Anthropic format) or Google format. For agentic use, `opencode serve` (an HTTP + SSE server with an OpenAPI spec) is a strong backend that OrchDesk can drive over HTTP.

### Cited Findings
- **Zen:**
  - Base URL is `https://opencode.ai/inference/v1`, authenticated with a Bearer Console API key. `GET /inference/v1/models` lists models.
  - Endpoint by family: `/responses` for GPT and Grok; `/messages` for Claude and some Qwen models; `/models/<id>` for Gemini; `/chat/completions` for DeepSeek, GLM, Kimi, MiniMax, Mistral, Qwen3.8 Max and the free models.
  - Free models are offered "for a limited time", e.g. Big Pickle, MiMo-V2.6-Flash Free and Nemotron 3 Ultra Free. Free models have different data-use terms.
  - Workspace and per-member monthly limits are available.
  - [OpenCode Zen docs](https://opencode.ai/docs/zen/)
- **Go:**
  - $10/month (Go) or $40/month (Go Plus, about 3× the limits). Single-member workspaces only.
  - Each model has a monthly dollar limit, with sub-caps of 20% per 5 hours and 50% per week. A "Use balance" option falls back to Zen pay-as-you-go.
  - Base URL is `https://opencode.ai/inference/go/v1`, with the same mix of endpoint formats and a `/models` list.
  - Clients should send their own User-Agent and a stable `x-opencode-session` header. Traffic is monitored for abuse.
  - Go is "designed for OpenCode and similar coding agents".
  - [OpenCode Go docs](https://opencode.ai/docs/go/)
- **`opencode serve`:**
  - Defaults to `127.0.0.1:4096`. The OpenAPI 3.1 spec is at `/doc`.
  - Endpoints: `GET /global/health`; sessions (`/session` with CRUD, abort, fork, revert); `POST /session/:id/message` (synchronous); `POST /session/:id/prompt_async` (returns 204); SSE on `/event` and `/global/event`.
  - Basic auth via `OPENCODE_SERVER_PASSWORD` (username defaults to `opencode`).
  - [OpenCode server docs](https://opencode.ai/docs/server/)
- **`opencode run`:**
  - Flags include `--format json` (raw events), `-m provider/model`, `--agent`, `-s/--session`, `-c`, `--attach http://localhost:4096` (reuses a running server and avoids MCP cold boots), `--dir` and `--auto` (auto-approve).
  - [OpenCode CLI docs](https://opencode.ai/docs/cli/)
- **Repo:** github.com/anomalyco/opencode, MIT licence, about 213.5k stars. The page does not show a redirect from sst/opencode, and the release dates were not visible — [GitHub anomalyco/opencode](https://github.com/anomalyco/opencode)

### Inferences
- OrchDesk can call a local `opencode serve` over HTTP and generate a C# client from `/doc` with NSwag or Kiota. This gives sessions, abort and SSE streaming without spawning a process per turn.
- OrchDesk can use OpenCode's own provider config to point at the PC's Ollama, so OpenCode becomes the unified agent loop for local and Zen models.
- For direct Zen/Go calls from C#, an OpenAI-compatible client only reaches `/chat/completions` models. Anthropic-format (`/messages`) and Responses-format models need separate clients.
- Model lists change often, so call `/models` at startup instead of hard-coding them.

### Gaps
- Tool-calling support per model on Zen/Go is not documented on these pages. It is presumably supported, since these are coding-agent models, but unverified.
- The exact JSON event schema of `opencode run --format json` was not documented.
- Whether the repo moved from sst to anomalyco via a GitHub redirect is unconfirmed.

## Q3. Detecting that the local LLM endpoint is offline, and secure remote access

### Takeaway
Probe a cheap, model-free endpoint with a short timeout (1–3 s connect, about 5 s total), e.g. `GET /v1/models` (OpenAI-compatible on Ollama, LM Studio, llama.cpp and vLLM) or Ollama's `/api/tags` / `/api/version`. Wrap it in a circuit breaker. Treat a Cloudflare Access 403, a tunnel 502/530 or connection refused as "offline".

Neither Ollama nor LM Studio (by default) authenticates requests, so never expose them directly. Put them behind a Cloudflare Tunnel with an Access "Service Auth" policy, or behind Tailscale.

### Cited Findings
- **Ollama:**
  - Binds to `127.0.0.1:11434` by default; change this with `OLLAMA_HOST`. `OLLAMA_ORIGINS` controls CORS.
  - Documented tunnel command: `cloudflared tunnel --url http://localhost:11434 --http-host-header="localhost:11434"`.
  - The FAQ does not document built-in auth.
  - `OLLAMA_KEEP_ALIVE` defaults to 5 minutes. A request with only `{"model": ...}` preloads a model, which is useful as a warm-up.
  - [Ollama FAQ](https://docs.ollama.com/faq)
- **LM Studio:**
  - The API server needs no auth by default. "Require Authentication" can be enabled with Bearer tokens created under Manage Tokens.
  - "Serve on Local Network" is off by default (from a third-party guide).
  - `lms server start` serves on `localhost:1234`.
  - LM Studio accepts both `x-api-key` and `Authorization: Bearer`.
  - [LM Studio auth docs (beta)](https://beta.lmstudio.ai/docs/developer/core/authentication), [LM Studio quickstart](https://lmstudio.ai/docs/developer/rest/quickstart), [LM Studio Claude Code integration](https://lmstudio.ai/docs/integrations/claude-code), [knightli guide](https://knightli.com/en/2026/07/11/lm-studio-openai-compatible-api-local-server-guide/)
- **Cloudflare Access service tokens:**
  - Send the `CF-Access-Client-Id` and `CF-Access-Client-Secret` headers. The policy action must be "Service Auth", otherwise Access redirects to an identity-provider login.
  - A single `Authorization` header mode is possible via `read_service_tokens_from_header`.
  - [Cloudflare service token docs](https://developers.cloudflare.com/access/service-auth/service-token/)

### Inferences
- Probe design:
  - Run `GET {base}/v1/models` every 15–30 s while idle, and immediately before routing a turn if the last success is more than 30 s old.
  - Use an `HttpClient` with `ConnectTimeout` about 2 s and an overall timeout about 5 s.
  - Use separate, longer timeouts for generation, since first-token latency includes loading the model on a cold start.
  - Classify:
    - DNS/TCP failure, HTTP 502/503/504/530 from Cloudflare, or an HTML login page (Access misconfigured) → offline.
    - 401/403 → auth error; alert the user rather than silently falling back.
- A `/v1/models` response proves the server is up, not that the model is loaded. Optionally send a 1-token warm-up or check Ollama `/api/ps`.
- On the Cloudflare path, a Worker in front of the tunnel can return a fast synthetic 503 when the origin is down, which makes detection quicker.
- Tailscale avoids exposing anything publicly and needs no headers. Check the endpoint at its MagicDNS name. Tailnet ACLs restrict which devices can reach port 11434/1234.
- Keep the service-token secret in Windows Credential Manager or DPAPI, not in plain config.

### Gaps
- Tailscale docs (Serve versus direct tailnet IP, ACL examples) were not fetched this session.
- The `/api/tags`, `/api/version` and `/v1/models` endpoints on Ollama are from prior knowledge; the FAQ page does not list them. Confirm in the Ollama API reference.
- llama.cpp `llama-server` `/health` and the vLLM `/health` endpoint were not verified this session.

## Q4. Claude Code subscription via CLI: limit detection, multiple profiles, ToS

### Takeaway
Run the unmodified `claude` binary as the user, signed in with the user's own subscription. Anthropic's docs explicitly allow this, but forbid third-party apps from offering Claude.ai login, routing traffic through subscription credentials, or handling session tokens.

Detect limits from stream-json, using:
- `system/api_retry` events (`error: "rate_limit"`),
- `rate_limit_event` (`status: "rejected"`; the schema varies by version),
- the final `result` (`is_error`) and the non-zero exit code.

Multiple accounts work via separate `CLAUDE_CONFIG_DIR` directories, an approach that is widely used but not officially documented.

### Cited Findings
- **Stream-json events:**
  - `claude -p --output-format stream-json --verbose [--include-partial-messages]` emits NDJSON. The last line is a `result` with the cost and the `session_id`.
  - Exit code is 0 on success and non-zero on failure. Run failures such as missing auth are printed as the result on stdout.
  - `system/api_retry` carries `attempt`, `max_retries`, `retry_delay_ms`, `error_status`, and an `error` category from: `rate_limit`, `billing_error`, `authentication_failed`, `overloaded`, and others.
  - `system/init` reports the model, tools and a `capabilities` array, which can be used for feature detection.
  - `--bare` is recommended for scripted runs, but bare mode does not use subscription OAuth: it requires `ANTHROPIC_API_KEY`. A SIGTERM exit returns code 143.
  - [Claude Code headless docs](https://code.claude.com/docs/en/headless)
- **`rate_limit_event`:**
  - Reported to carry `rate_limit_info.status` (`allowed`, `allowed_warning` or `rejected`), `rate_limit_type` (`five_hour`, `seven_day`), utilization and reset timestamps.
  - A different cheatsheet says the event is informational and lists other fields.
  - There is no official Anthropic schema page, so verify against your installed CLI version.
  - [ClaudeCode Elixir SDK hexdocs](https://claude-code.hexdocs.pm/ClaudeCode.Message.RateLimitEvent.html), [Untether stream-json cheatsheet](https://littlebearapps.com/help/untether/claude-stream-json-cheatsheet/), [claude-mem release notes](https://newreleases.io/project/github/thedotmack/claude-mem/release/v13.23.0)
- **ToS (official text):**
  - "OAuth authentication is intended exclusively for purchasers of Claude Free, Pro, Max, Team, and Enterprise subscription plans and is designed to support ordinary use of Claude Code and other native Anthropic applications."
  - Developers building products, including Agent SDK products, "should use API key authentication".
  - "Anthropic does not permit third-party developers to offer Claude.ai login into their own applications, or to route requests through Free, Pro, or Max plan credentials on behalf of their users… may not collect, store, or intermediate Claude.ai credentials or session tokens."
  - It does not prevent "an end user from signing in to the unmodified Claude Code binary with their own Claude subscription".
  - "Advertised usage limits for Pro and Max plans assume ordinary, individual usage of Claude Code and the Agent SDK."
  - Enforcement may happen "without prior notice".
  - [Claude Code legal and compliance](https://code.claude.com/docs/en/legal-and-compliance)
- **Enforcement history:** server-side blocking of subscription OAuth in third-party clients began around January 2026, with the policy documented in February 2026. OpenClaw subscription use was later cut off; the date is unclear across sources — [WinBuzzer](https://winbuzzer.com/2026/02/19/anthropic-bans-claude-subscription-oauth-in-third-party-apps-xcxwbn/), [Gigazine](https://gigazine.net/gsc_news/en/20260220-anthropic-third-party-block), [CyberSecurityNews](https://cybersecuritynews.com/claude-ends-openclaw-subscriptions/)
- **`CLAUDE_CONFIG_DIR`:** each directory holds its own credentials, settings, history and plugins. Community tools (claude-multiprofile, CCS, ditto) automate this. One source calls the variable "stable but undocumented" — [claude-multiprofile](https://github.com/jmdarre-v/claude-multiprofile), [wmedia.es](https://wmedia.es/en/tips/claude-code-multiple-profiles-config-dir), [CCS docs](https://docs.ccs.kaitran.ca/providers/claude-accounts)

### Inferences
- **Compliant pattern:**
  - OrchDesk spawns the official `claude` CLI, which the user has logged into through Anthropic's own flow.
  - OrchDesk never reads `~/.claude/.credentials.json` or the keychain token, and never proxies the OAuth token into its own HTTP calls.
  - Usage stays personal (one user, their own machine).
  - Heavy unattended or parallel automation may exceed "ordinary, individual usage"; this is a grey area that should be flagged in the UI.
- **Using multiple subscription accounts to rotate around limits** is technically possible with `CLAUDE_CONFIG_DIR`, but it is ToS-risky: it can look like evading limits, and each account must belong to a legitimate purchaser. Recommend supporting profiles for work/personal separation, not for auto-rotation.
- **Limit detection heuristic:**
  1. Watch for `system/api_retry` with `error == "rate_limit"`, or `rate_limit_event` with status `rejected`. Parse the reset time if present.
  2. On a `result` with `is_error: true` and text matching the usage limit (e.g. "limit reached… resets"), mark the Claude backend cooled down until the reset time and fall back to OpenCode, Codex or local.
  3. Log raw events for the installed version, since the schemas drift.
- Do not use `--bare` with a subscription, because bare mode ignores OAuth.

### Gaps
- No official schema for `rate_limit_event`, and no verified exact text of the usage-limit `result` message for October 2026 CLI versions.
- The Codex CLI (`codex exec --json`) event format and ChatGPT-plan limit signalling were not researched this session.
- The consumer-terms page itself (anthropic.com/legal/consumer-terms) was not fetched; the quotes come from the Claude Code legal page.
