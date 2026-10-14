# OrchDesk

OrchDesk is a Windows desktop coding assistant that works like Claude Code but can use several models. You choose a project folder and describe a change. The model you pick reads the code, edits it, builds it and runs the tests until the job is done. It asks you before anything risky.

It runs as one `OrchDesk.exe`, with no installer and no admin rights, so it works on the company laptop as well as your PC.

## What it can talk to

| Model | How OrchDesk reaches it | Who runs the tools |
|---|---|---|
| Models on your desktop PC (Qwen, Gemma, …) | OpenAI-compatible URL, e.g. Ollama `http://127.0.0.1:11434/v1` or LM Studio `:1234/v1` | OrchDesk's own agent loop |
| OpenCode Go / Zen | `https://opencode.ai/zen/go/v1` plus your API key | OrchDesk's own agent loop |
| Claude (Outlook and Gmail subscriptions) | `claude -p --output-format stream-json`, one `CLAUDE_CONFIG_DIR` per account | Claude Code itself |
| Codex CLI, OpenCode CLI | `codex exec -`, `opencode run` (off by default) | The CLI itself |

**Auto mode** tries the models in order. The default order is PC → Claude Outlook → Claude Gmail → OpenCode. Every 30 seconds OrchDesk checks each model's health, using `/models` for HTTP models and a PATH check for CLIs.
- **PC off or unreachable:** OrchDesk skips the PC model and uses the next one in the chain. The message you sent is retried there automatically, so nothing is lost.
- **Usage limit hit:** that account rests for 5 hours (configurable), and the next account takes over.

## Modes

- **Chat:** one model, with streaming replies. Each tool call shows as a one-line trace you can click to see its output.
- **Supervisor:** Claude plans and reviews but doesn't edit. A cheaper model (your PC or OpenCode) writes the code. OrchDesk runs the tests between rounds and sends failures back for a review-and-fix pass, up to N rounds.
- **Cost router:** tries the cheapest model first, checks the result with the tests, and moves up a tier only if they fail.
- **Overnight queue:** add tasks during the day and they run unattended at night.
  - Each task gets its own git worktree and an `orchdesk/<date>/<id>` branch, so your checkout and `main` are never touched.
  - Models rotate on usage limits or when the PC is offline.
  - OrchDesk runs the tests itself and retries failures, then commits the result on the task's branch.
  - Pushing is optional, only for `orchdesk/*` branches, and only after a secret scan.
  - A Markdown morning report summarises everything.
  - **Schedule nightly** registers a Windows scheduled task that wakes the PC and starts `OrchDesk.exe --overnight`.

## Safety

- File tools only work inside the chosen folder. `..`, absolute paths elsewhere, and symlinks that point outside are all rejected.
- These run without asking:
  - reading, searching and editing files inside the folder;
  - build and test commands (`dotnet build/test`, `npm test`, `pytest`, `cargo test`, …);
  - `git status`, `git diff` and `git commit`.
- These always ask first, using an Approve or Deny card:
  - deleting files;
  - installing packages;
  - network commands;
  - `git push`;
  - destructive git;
  - paths outside the folder;
  - commands chained with `&&`, `;`, `|` or `>`;
  - any other shell command.
- In overnight mode nobody is there to approve, so anything that would ask is refused and noted in the report.
- Commands have timeouts and their output is capped. If a model repeats the exact same tool call three times in a row, OrchDesk stops the loop.
- `web_fetch` is GET-only, capped at 200 KB, refuses local and private addresses, and labels the page content as untrusted data.
- API keys are stored in `%APPDATA%\OrchDesk\config.json`, encrypted with Windows DPAPI for your Windows user. They are never written to the repo.
- When OrchDesk starts the Claude CLI it removes `ANTHROPIC_API_KEY`, `ANTHROPIC_AUTH_TOKEN` and `ANTHROPIC_BASE_URL` from the environment, so it can only use your subscription. It never uses `--bare`, which would ignore the subscription login.

### A note on Claude subscriptions

Anthropic's terms allow running your own unmodified `claude` program with your own subscription, and that is all OrchDesk does. Its usage limits assume ordinary individual use, and switching between two accounts *specifically to get around limits* is a terms-of-service risk. The Gmail profile is there as a fallback. Decide for yourself how you use it.

## Project layout

```
src/OrchDesk.Core/     providers, agent loop, tools, permissions, rotation, supervisor, overnight runner (no UI)
src/OrchDesk.App/      WPF app: chat, overnight queue, models & accounts, settings, themes
tests/OrchDesk.Tests/  xUnit tests (tools, path safety, permissions, agent loop, failover, parsers)
docs/                  original plan and the integration research report
```

## Build

You need the .NET 10 SDK. .NET 8 support ends in November 2026, so the project targets .NET 10.

```powershell
dotnet test
dotnet publish src/OrchDesk.App -c Release -r win-x64 --self-contained -p:PublishSingleFile=true -o publish
# → publish\OrchDesk.exe
```

The GitHub workflow builds the exe on every push to `main`. It attaches the exe to a GitHub Release when you push a `v*` tag. See [LAPTOP-SETUP.md](LAPTOP-SETUP.md) for setting up a second machine.

## Icon and logo

The app currently uses a drawn placeholder mark: a terracotta ring with a peach orbit dot. To use your real icon, save it as `src/OrchDesk.App/Assets/orch-app-icon.ico`. The project picks it up automatically on the next build. To make the `.ico` from your 512×512 PNG, any PNG-to-ICO converter works (include the 16, 32, 48 and 256 px sizes).

## Next steps suggested by the research

The full report is in `docs/research/`. The main suggestions are:
- render chat replies as Markdown, using MdXaml;
- show diffs with DiffPlex.Wpf;
- add a shadow-git checkpoint before each run so it can be undone;
- drive OpenCode through `opencode serve` and Codex through `codex app-server` to get richer events;
- optionally add MCP tools via the official C# SDK.

## Headless verification

Run `OrchDesk.exe --selftest <folder> [--provider local-pc]`. No window is opened. The default checks LM Studio at `http://127.0.0.1:1234/v1` with `qwen/qwen3.5-9b`; other provider IDs use enabled OpenAI-compatible settings. Each check writes pass/fail and a fixed detail to `<folder>\selftest.json`. Exit codes: 0 all passed, 1 a check failed, 2 invalid setup. Provider responses and secrets are never included in the report.

The checks cover a real model reply, model-driven create/edit tools in a unique subfolder, file read/edit/save, delete approval, account health and simulated rotation, a real supervisor verification command with fake planner/worker, and a live GET of example.com. Existing hello.txt is preserved. `web_fetch` reads at most 200,000 bytes, refuses private DNS/IP addresses and redirects, and fences returned data as untrusted.
