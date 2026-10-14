# Setting up OrchDesk on a machine (PC or laptop)

## 1. Get the exe

**Without the .NET SDK** (the usual case on a company laptop): download `OrchDesk.exe` from the repo's latest GitHub Release. If there's no release yet, download it from the latest run of the "Build and release" workflow, under Artifacts. Put it anywhere you can write to, for example `%LOCALAPPDATA%\OrchDesk\OrchDesk.exe`.

**With the SDK:**

```powershell
git clone https://github.com/manniewarriner/WebDev.git OrchDesk
cd OrchDesk
dotnet publish src/OrchDesk.App -c Release -r win-x64 --self-contained -p:PublishSingleFile=true -o publish
```

No installer or admin rights are needed. On first launch the exe unpacks a few WPF files into `%TEMP%\.net`, which is normal.

## 2. Claude profiles (subscription, no API credits)

Install the Claude Code CLI (`npm i -g @anthropic-ai/claude-code`, or the native installer). Then log in once per account:

```powershell
$env:CLAUDE_CONFIG_DIR = "$HOME\.claude-outlook"; claude   # then type /login and use the Outlook account
$env:CLAUDE_CONFIG_DIR = "$HOME\.claude-gmail";   claude   # then /login with the Gmail account
```

These folder paths are the defaults OrchDesk expects. You can change them under **Settings → Models**.

## 3. Models on the desktop PC

On the **PC**, run Ollama (`http://127.0.0.1:11434/v1`) or LM Studio (`http://127.0.0.1:1234/v1`). In **Settings → Models → Desktop PC**, set the base URL and the model name, for example `qwen2.5-coder:14b`.

On the **laptop**, pick one:
- **A. Simplest:** disable the Desktop PC model, or just leave it. When the laptop can't reach it, it shows as *PC unreachable* and Auto mode uses the cloud models.
- **B. Reach the PC remotely:** put the PC's model server behind your Cloudflare `orch-relay` Worker or a Tunnel, with Cloudflare Access set to *Service Auth*. Then add a model on the laptop:
  - Base URL: `https://<your-relay>/v1`
  - **Runs on my PC:** ticked
  - The Access client ID and secret, in their two fields

  When the PC is off, the relay fails and OrchDesk falls back to the cloud models. Never expose Ollama or LM Studio to the internet without Access in front of them.

## 4. OpenCode Go

In **Settings → Models → OpenCode Go**, paste your API key. The model field defaults to `glm-5.2`. You can see the other models at `https://opencode.ai/zen/go/v1/models`.

Keys are encrypted with DPAPI for your Windows user. That's why each machine needs its keys entered once: the encrypted config can't be copied between machines.

## 5. Overnight runs

1. Open **Overnight queue**, add tasks, and set the start time.
2. Click **Schedule nightly**. This creates the Windows scheduled task `OrchDesk\Overnight`. It wakes the PC and runs `OrchDesk.exe --overnight`.
3. Leave the PC plugged in and signed in (locked is fine).
4. In the morning, click **Open morning report**. To review a task's branch: `git log -p HEAD..orchdesk/<date>/<id>`. To merge it: `git merge orchdesk/<date>/<id>`.

If you move the exe, click **Schedule nightly** again so the task points to the new location.
