# OrchDesk: build plan

## What the app is
A local C# Windows desktop app, built like your CRM, that runs on whichever machine you open it on (PC or laptop). It's a Claude Code-style coding assistant that can use several models. You chat with it, point it at a folder, and the chosen model reads, edits, runs and tests code there until the job is done, asking you before anything risky.

It's published as a self-contained single-file .exe (no install, no admin rights), so it runs on the company laptop.

## Stack
- .NET 8, C#, using the same UI framework as your CRM (WPF is the simplest for a chat UI; WinForms is fine if that's what the CRM uses).
- Publish: `dotnet publish -c Release -r win-x64 --self-contained -p:PublishSingleFile=true`
- Settings and keys go in `%APPDATA%\OrchDesk\config.json`, with secrets encrypted using DPAPI (`ProtectedData`). Never put keys in the repo.

## Core parts
1. **Chat window**
   - Message list, input box, model picker and a folder picker.
   - Responses stream in, and you can see each tool call as it happens (e.g. "Reading Program.cs", "Running tests").
   - A Stop button.
2. **Providers**, all behind one interface `IModelProvider { StreamChatAsync(messages, tools) }`:
   - **Local PC models** (Bionic, Qwen, Gemma): an OpenAI-compatible HTTP endpoint on the PC (e.g. `http://127.0.0.1:<port>/v1`).
   - **OpenCode Go and free models**: the OpenAI-compatible API at `https://opencode.ai/zen/go/v1` with your key, or by running the `opencode run` CLI.
   - **Claude (subscription, no API credits)**: run the `claude` CLI in print mode (`claude -p --output-format stream-json`) with `CLAUDE_CONFIG_DIR` set to the Gmail or Outlook profile. Remove any `ANTHROPIC_API_KEY` from the environment so it can never use paid credits.
   - **Codex**: the `codex` CLI, if it's installed.
3. **Agent loop** (for models without their own agent mode, i.e. the local ones and OpenCode via API). Send the task with tool definitions, run each tool the model calls, return the results, and repeat until it gives a final answer or reaches a step limit (e.g. 40). Claude and OpenCode can use their own CLI agent modes instead.
4. **Tools for the models**:
   - `list_files`, `read_file`, `search` (grep)
   - `edit_file` (find-and-replace or diff), `write_file`
   - `run_command` (runs in the project folder, with a timeout, and captures output), used for build and test
   - `git_status`, `git_diff`, `git_commit`
   - `web_fetch` (read-only, with a size limit; page content is treated as data, not instructions)
5. **Permissions**
   - Automatically allowed: reading, editing files inside the chosen folder, and running build and test commands.
   - Ask first, with an Approve or Deny card: deleting files, anything outside the folder, installing packages, `git push`, and network commands.
6. **Accounts panel**
   - Shows each provider's status (connected or not), the active Claude profile, and when the last usage limit was hit.
   - Rotation: if Claude Outlook hits its limit, switch to Claude Gmail, then OpenCode, then the local models.
7. **Supervisor (the Y1 logic)**: an optional mode where Claude plans and checks the work, hands the bulk coding to a cheaper model (local or OpenCode), runs the tests, and loops on failures. It keeps the error context and stops after N rounds.
8. **Cost router (optional)**: try the cheapest model first, verify with tests, and step up a tier on failure.

## Reaching your PC's models from the laptop
Choose one:
- **A. Simplest:** on the laptop, use only the cloud models (Claude and OpenCode). Use the local models only on the PC.
- **B. Remote PC models:** keep the existing Cloudflare `orch-relay` Worker as a private API to the PC's model endpoint, protected by Cloudflare Access with a service token stored in the app's encrypted config. The laptop app calls it like any other provider and falls back to cloud models when the PC is off.

## Look and feel
- Your logo, in terracotta, peach and dark brown.
- Two themes: a warm dark theme and a cream light theme.
- A clean font, e.g. Inter or Segoe UI Variable.
- While a model is thinking, the logo animates with moving "..." dots.
- App icon: `orch-app-icon.ico` (in your Gmail draft).

## Repo layout
```
OrchDesk/
  OrchDesk.sln
  src/OrchDesk.App/          UI (windows, views, themes, assets)
  src/OrchDesk.Core/         providers, agent loop, tools, permissions, config
  tests/OrchDesk.Tests/      xUnit tests (tools, loop with a fake provider, path safety)
  README.md  LAPTOP-SETUP.md  .gitignore (bin/ obj/ *.user config secrets)
```

## Build order
1. A chat window and one provider (a local model or OpenCode) with streaming replies.
2. File tools, the folder picker and the agent loop. Test: "create hello.txt, edit it, run a test".
3. The Claude CLI provider with the two profiles and rotation.
4. The permission cards, plus the shell and git tools.
5. The accounts panel and settings, with DPAPI-protected keys.
6. The supervisor mode.
7. Theme, logo animation and the icon.
8. Publish the single-file exe, push it to a private GitHub repo, and attach the exe to a Release.
9. Laptop: `git clone` and either `dotnet publish`, or download the Release exe if there's no SDK.

## Security checklist
- No keys in the repo; run a secret scan before every push.
- File tools reject `..` and anything outside the chosen folder.
- Commands have timeouts, and nothing runs without approval when it's outside the folder.
- Web content is treated as data only.
- The Claude CLI runs with API-key environment variables removed, so it stays on the subscription only.
