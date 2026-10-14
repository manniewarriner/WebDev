# OrchDesk operation

Settings → Integrations provides service cards with Test connection, status and Save. Tests use unsaved fields. Save stores secrets as Windows-user DPAPI ciphertext in `%APPDATA%\OrchDesk\config.json`. In-app saves and external config replacements reload providers live.

LM Studio uses `http://127.0.0.1:1234/v1`, with `qwen/qwen3.5-9b` and `google/gemma-4-12b-qat`. Ollama uses its `/v1` endpoint; enter an installed model name. Remote PC accepts an orch-relay OpenAI-compatible base URL and Cloudflare Access client ID/secret. Both Access headers accompany health probes and every chat/tool turn.

OpenCode Go uses `https://opencode.ai/inference/go/v1`, model `glm-5.2`, and your Go key. This adapter supports chat-completions models. Models requiring Responses, Messages or Gemini protocols require separate adapters. Endpoint reference: https://opencode.ai/v2/docs/console/go/.

Claude profiles pass the chosen directory to the installed CLI. OrchDesk never opens or edits profile files. Test connection runs only `claude --version`, without targeting the profile. The CLI handles subscription authentication and native tools during chat. Codex/OpenCode CLI require installed and authenticated executables. Windows npm batch shims are supported; prompts go through stdin.

Choose a folder, then Open file, edit and Save. API chat models use OrchDesk's permission-controlled tools; CLI agents use native tools.

`OrchDesk.exe --smoketest` writes `%APPDATA%\OrchDesk\smoketest\smoketest.json`, containing pass/fail/skip per provider. Configured providers get connection, chat and file write/read tests in fresh folders. Missing/disabled integrations skip. Reports exclude credentials and provider error bodies. Exit codes: 0 = no failures, 1 = failed checks, 2 = setup failure.

For UI overnight work, select an API manager, configure worker ID, max rounds and test command in Settings → Overnight run, choose a folder, and send `/overnight <goal>`. Shutdown PC when done is exposed in the chat header and overnight settings, off by default.

Script example:

```powershell
OrchDesk.exe --overnight-goal "Implement the feature" --workspace "C:\Projects\Example" --until 08:00 --max-rounds 4 --manager local-pc --workers local-pc,local-pc-gemma --shutdown-when-done
```

Start on a clean working tree. Each round plans, lets API workers edit, builds, tests, commits green changes to the original branch, and asks the manager to inspect goal completion. Never pushes. Native CLI workers are excluded from unattended rounds because their internal tools cannot be gated by OrchDesk. The until time is local; elapsed times mean the next day. The user's `.dotnet` SDK directory is added to PATH.

Each run writes `%APPDATA%\OrchDesk\runs\<id>\checkpoint.json`, `progress.log` and `morning-summary.md`. Interrupted tool actions require review rather than replay. Running checkpoints are inspected at UI startup. Optional shutdown runs `shutdown /s /t 120` after the morning summary is written; paused/error runs do not shut down. Cancel a scheduled shutdown with `shutdown /a`.
