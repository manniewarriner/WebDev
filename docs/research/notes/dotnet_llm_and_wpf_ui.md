# .NET LLM client/agent libraries and WPF UI building blocks for OrchDesk (C#/.NET 8 WPF, self-contained single-file exe) — state as of 2026-10-14

Method note: versions, publish dates, licences and target frameworks were read directly from the NuGet v3 API (api.nuget.org registration/flatcontainer endpoints) on 2026-10-14. Star counts and archive status come from a GitHub repository search done the same day. "NuGet API" below means `https://api.nuget.org/v3-flatcontainer/<id>/index.json` and `https://api.nuget.org/v3/registration5-semver1/<id>/index.json`.

## Q1: Which LLM-side libraries exist, and how mature are they? (MEAI, SK / Agent Framework, OpenAI SDK, Anthropic SDKs, OllamaSharp, MCP SDK)

### Takeaway
The .NET LLM stack has settled on Microsoft.Extensions.AI (MEAI) `IChatClient` as the common abstraction. The official OpenAI SDK, the official Anthropic SDK, OllamaSharp, the MCP C# SDK and Microsoft Agent Framework all plug into it. Everything listed below is stable (non-preview), MIT or Apache-2.0, and targets net8.0 or netstandard2.0, so all of it works with .NET 8 WPF. Semantic Kernel has been replaced by Microsoft Agent Framework. The name "Anthropic" on NuGet now refers to the official SDK, not the earlier community package.

### Cited Findings

**Microsoft.Extensions.AI (+ .Abstractions, + .OpenAI)** — repo https://github.com/dotnet/extensions (3.3k stars, active)
- Latest stable is 10.11.0, published 2026-10-14, MIT. It targets net462, netstandard2.0, net8.0, net9.0 and net10.0. `Microsoft.Extensions.AI.OpenAI` is also stable at 10.11.0. — [NuGet API](https://api.nuget.org/v3-flatcontainer/microsoft.extensions.ai/index.json)
- `Microsoft.Extensions.AI.Abstractions` contains `IChatClient` and `IEmbeddingGenerator`. `Microsoft.Extensions.AI` adds middleware: automatic function tool invocation, OpenTelemetry and caching. Apps should reference `Microsoft.Extensions.AI` together with a provider implementation. — [Microsoft Learn: MEAI](https://learn.microsoft.com/en-us/dotnet/ai/microsoft-extensions-ai)
- `IChatClient` covers multi-modal messages, returned either complete or streamed incrementally. — [Microsoft Learn: MEAI](https://learn.microsoft.com/en-us/dotnet/ai/microsoft-extensions-ai)
- The MEAI.OpenAI README shows: wrap an OpenAI `ChatClient` with `.AsIChatClient()`, stream with `GetStreamingResponseAsync` in `await foreach`, and add tools with `ChatClientBuilder(...).UseFunctionInvocation().Build()` plus `AIFunctionFactory.Create(...)` in `ChatOptions.Tools`. The package description says it supports "OpenAI-compatible endpoints". 10.11.0 requires `OpenAI` >= 2.14.0. — [NuGet: Microsoft.Extensions.AI.OpenAI](https://www.nuget.org/packages/Microsoft.Extensions.AI.OpenAI)

**OpenAI official .NET SDK** — https://github.com/openai/openai-dotnet (2.7k stars, active)
- Latest is `OpenAI` 2.14.0, published 2026-09-15, MIT. It targets netstandard2.0, net8.0 and net10.0. — [NuGet API](https://api.nuget.org/v3/registration5-semver1/openai/index.json)
- To reach an alternative endpoint (a proxy or a self-hosted OpenAI-compatible LLM), set `OpenAIClientOptions.Endpoint` and pass an `ApiKeyCredential`. Chat streaming uses `CompleteChatStreamingAsync`, which returns `AsyncCollectionResult<StreamingChatCompletionUpdate>`. The README's Chat Completions tool-calling sample is non-streaming. The SDK has both `ChatClient` (Chat Completions) and `ResponsesClient` (Responses API). — [openai-dotnet README](https://github.com/openai/openai-dotnet)
- In community examples, LM Studio works at `http://localhost:1234/v1` and Ollama at `http://localhost:11434/v1` through the same Endpoint override, and the model name and key matter little for local servers. These sources are informal or third-party. — [codeinside blog (2024)](https://blog.codeinside.eu/2024/09/17/openai-api-lmstudio-ollama/); [Reveal BI custom endpoints](https://help.revealbi.io/ai/providers-custom-endpoints/)

**Anthropic official C# SDK** — https://github.com/anthropics/anthropic-sdk-csharp (335 stars, active; created 2025-08)
- Package `Anthropic`. From v10 onward it is the official Anthropic SDK. Versions 3.x and below were the community tryAGI SDK, which moved to the `tryAGI.Anthropic` package. Requires .NET Standard 2.0+. — [Claude docs: C# SDK](https://platform.claude.com/docs/en/api/sdks/csharp)
- Latest is 12.57.0, published 2026-10-14, MIT, authored by "Anthropic". It targets netstandard2.0, net8.0 and net9.0, and depends on `Microsoft.Extensions.AI.Abstractions` 10.5.1 and `System.Net.ServerSentEvents`. — [NuGet API](https://api.nuget.org/v3-flatcontainer/anthropic/12.57.0/anthropic.nuspec)
- Ships an `IChatClient` implementation via `client.AsIChatClient("model")`, which composes with `.UseFunctionInvocation()` and MCP C# SDK tools. Streaming uses `Messages.CreateStreaming` (returns `IAsyncEnumerable`). Defaults are 2 retries and a 10-minute timeout. `BaseUrl` / `ANTHROPIC_BASE_URL` can be overridden. Separate packages exist: `Anthropic.Bedrock`, `Anthropic.Vertex`, `Anthropic.Foundry`, `Anthropic.Aws` (beta). — [Claude docs: C# SDK](https://platform.claude.com/docs/en/api/sdks/csharp)
- Churn: the SDK "generally follows SemVer", but some backward-incompatible changes may ship as minor versions. — [Claude docs: C# SDK](https://platform.claude.com/docs/en/api/sdks/csharp)

**Community Anthropic SDKs**
- `Anthropic.SDK` (tghamm) is 5.10.0, published 2026-02-20, MIT. It describes itself as unofficial. The repo has 222 stars and is still pushed to. — [NuGet API](https://api.nuget.org/v3/registration5-semver1/anthropic.sdk/index.json); [GitHub tghamm/Anthropic.SDK](https://github.com/tghamm/Anthropic.SDK)
- `tryAGI.Anthropic` is 3.8.3 stable (2026-06-17), with 3.8.4-dev prereleases. The latest stable targets only net10.0, so it is not consumable from .NET 8. The repo has 43 stars. — [NuGet API](https://api.nuget.org/v3/registration5-semver1/tryagi.anthropic/index.json); [GitHub tryAGI/Anthropic](https://github.com/tryAGI/Anthropic)

**OllamaSharp** — https://github.com/awaescher/OllamaSharp (1.4k stars, 1 open issue, active)
- 5.5.0, published 2026-09-30. It targets netstandard2.0/2.1 and net8/9/10. Repo topics include `ichatclient` and `microsoft-extensions-ai`. — [NuGet API](https://api.nuget.org/v3/registration5-semver1/ollamasharp/index.json); [GitHub](https://github.com/awaescher/OllamaSharp)
- The NuGet registration has no `licenseExpression` (the licence is probably a file). Check the repo before relying on it. — [NuGet API](https://api.nuget.org/v3/registration5-semver1/ollamasharp/index.json)

**ModelContextProtocol C# SDK** — https://github.com/modelcontextprotocol/csharp-sdk (4.6k stars; "official C# SDK ... maintained in collaboration with Microsoft")
- `ModelContextProtocol` 2.2.0, published 2026-08-13, Apache-2.0. It targets netstandard2.0 and net8/9/10. — [NuGet API](https://api.nuget.org/v3/registration5-semver1/modelcontextprotocol/index.json)
- MCP client tools (`McpClient.ListToolsAsync()`) can go directly into `ChatOptions.Tools` of any `IChatClient`. Anthropic's docs show this with `HttpClientTransport`. — [Claude docs: C# SDK](https://platform.claude.com/docs/en/api/sdks/csharp)

**Semantic Kernel / Microsoft Agent Framework**
- `Microsoft.SemanticKernel` 1.81.0 (2026-10-06, MIT, 28.7k stars) is still shipping. — [NuGet API](https://api.nuget.org/v3/registration5-semver1/microsoft.semantickernel/index.json); [GitHub](https://github.com/microsoft/semantic-kernel)
- `Microsoft.Agents.AI` 1.25.0 was published 2026-10-14 (MIT). It targets net472, netstandard2.0 and net8/9/10. `Microsoft.Agents.AI.OpenAI` and `Microsoft.Agents.AI.Anthropic` are also stable at 1.25.0. The anthropic package left preview at 1.24.0, after 1.2x preview builds through late September 2026. Repo: https://github.com/microsoft/agent-framework (14.1k stars, 759 open issues). — [NuGet API](https://api.nuget.org/v3-flatcontainer/microsoft.agents.ai/index.json); [GitHub search](https://github.com/microsoft/agent-framework)
- Agent Framework is described as the successor to Semantic Kernel and AutoGen, made by the same team ("Semantic Kernel v2.0"). SK v1.x is supported with critical bug and security fixes for at least one year after Agent Framework GA. Most new feature work goes to Agent Framework. — [devblogs: Semantic Kernel and Microsoft Agent Framework](https://devblogs.microsoft.com/semantic-kernel/semantic-kernel-and-microsoft-agent-framework/)
- Agent Framework 1.0 GA was in early April 2026, dated April 2 or April 3 depending on the source. This comes only from secondary sources. — [atlan.com](https://atlan.com/know/ai-agent/microsoft/agent-framework/); [Azure AI labs](https://labs.ai.azure.com/?p=1155)

**Platform note**
- .NET 8 (and .NET 9) reach end of support on 2026-11-10. Microsoft recommends .NET 10 LTS, supported to November 2028. — [devblogs: .NET 8 & 9 end of support](https://devblogs.microsoft.com/dotnet/dotnet-8-9-end-of-support/)

### Inferences
- Recommended stack for OrchDesk: `Microsoft.Extensions.AI` 10.x + `Microsoft.Extensions.AI.OpenAI` 10.x + `OpenAI` 2.14.x as the single provider path for every OpenAI-compatible endpoint (OpenCode Zen/Go, Ollama `/v1`, LM Studio, llama.cpp `llama-server`). Use `new OpenAIClient(new ApiKeyCredential(key ?? "none"), new OpenAIClientOptions{ Endpoint = baseUrl }).GetChatClient(model).AsIChatClient()`, then `.AsBuilder().UseFunctionInvocation()` only if OrchDesk wants MEAI to run the tool loop. A coding agent with approval gates is usually better off running its own loop, reading `FunctionCallContent` from streamed `ChatResponseUpdate`s. Suggested version ranges: `Microsoft.Extensions.AI` [10.11.0,11.0.0), `Microsoft.Extensions.AI.OpenAI` [10.11.0,11.0.0), `OpenAI` [2.14.0,3.0.0).
- Add `Anthropic` [12.57.0,13.0.0) only if native Anthropic Messages features are needed (prompt caching, thinking blocks). It exposes `IChatClient` too, so the rest of OrchDesk stays provider-agnostic. Avoid `Anthropic.SDK` and `tryAGI.Anthropic`. Note: the package ID `Anthropic` changed owner, so any old reference pinned at <=3.x is the tryAGI code.
- OllamaSharp is optional. Ollama's OpenAI-compatible `/v1` endpoint already works through the OpenAI path. Use OllamaSharp [5.5.0,6.0.0) only for Ollama-native features (model pull/list, `keep_alive`, native `think`).
- Add `ModelContextProtocol` [2.2.0,3.0.0) when OrchDesk acts as an MCP client. Its tools are `AIFunction`s that drop into `ChatOptions.Tools`.
- Do not adopt Semantic Kernel for new code. Agent Framework (`Microsoft.Agents.AI` 1.25.x) is optional and heavy for a single-agent desktop app. MEAI alone covers streaming, tool calling and middleware. Consider Agent Framework only if OrchDesk needs multi-agent workflows or checkpointing.
- Replacing the hand-written SSE client: yes for OpenAI-compatible providers. It removes parsing of `data:` frames, tool-call delta assembly and retry logic, and keeps one abstraction for all providers. Keep a thin escape hatch: `ChatOptions.RawRepresentationFactory` / `AdditionalProperties` and the underlying `ChatClient` for vendor-specific fields such as `reasoning_content` from llama.cpp/DeepSeek-style servers. MEAI may not surface those fields, so test against each target server.
- Because .NET 8 support ends 2026-11-10, OrchDesk should plan to retarget to net10.0-windows. All the libraries above already ship net10.0 assets.

### Gaps
- No primary source was found for how MEAI.OpenAI handles non-standard streaming fields (`reasoning_content`, llama.cpp `timings`) from OpenAI-compatible servers. This needs a hands-on test.
- OpenCode Zen/Go compatibility with the OpenAI .NET SDK was not verified. No source was found.
- OllamaSharp licence is not stated in the NuGet metadata. Historically it is MIT, but that was not verified this session.
- The exact Agent Framework GA date is not confirmed by a primary Microsoft source here. The devblog fetched dates from the preview announcement.
- Trimming/AOT annotations for the OpenAI and Anthropic SDKs were not documented in the pages read. This is moot for WPF, which cannot be trimmed (see Q3).

## Q2: Which WPF UI libraries should OrchDesk use (Fluent controls, markdown, code view, diffs, WebView2, MVVM, tray, toasts)?

### Takeaway
WPF-UI, AvalonEdit, DiffPlex(.Wpf), CommunityToolkit.Mvvm and Markdig are all current and support net8.0-windows. Markdig.Wpf is stale (last release 2021). MdXaml is the maintained WPF-native markdown renderer. H.NotifyIcon.Wpf 2.4.1 dropped net8 assets, so .NET 8 projects should pin 2.3.2. Microsoft.Toolkit.Uwp.Notifications is frozen at 2022 and its repo is archived. Windows App SDK `AppNotificationManager` is Microsoft's recommended toast API for unpackaged WPF.

### Cited Findings

**WPF-UI (lepoco/wpfui)** — https://github.com/lepoco/wpfui
- `WPF-UI` 4.3.0, published 2026-05-04, MIT. It targets net462/472/481 and net8.0-, net9.0- and net10.0-windows7.0. The repo has 9.7k stars, 459 open issues, and was pushed 2026-10-14. — [NuGet API](https://api.nuget.org/v3/registration5-semver1/wpf-ui/index.json); [GitHub search result](https://github.com/lepoco/wpfui)

**Markdown rendering**
- `Markdig` 1.4.0, 2026-09-20, BSD-2-Clause. It targets net462, netstandard2.0/2.1, net8.0 and net10.0. The repo xoofx/markdig has 5.3k stars and is active. — [NuGet API](https://api.nuget.org/v3/registration5-semver1/markdig/index.json); [GitHub](https://github.com/xoofx/markdig)
- `Markdig.Wpf` 0.5.0.1 was last published 2021-01-15 (MIT). It targets netcoreapp3.1, net452 and net5.0-windows, with no newer release. — [NuGet API](https://api.nuget.org/v3/registration5-semver1/markdig.wpf/index.json)
- `MdXaml` stable is 1.27.0. The latest prerelease is 2.0.0-pre202603081301 (2026-03-08), MIT, targeting net462 and net8.0-windows7.0. whistyun/MdXaml has 339 stars and was pushed 2026-10-09. It renders Markdown to a WPF FlowDocument. — [NuGet API](https://api.nuget.org/v3-flatcontainer/mdxaml/index.json); [GitHub](https://github.com/whistyun/MdXaml)

**AvalonEdit** — https://github.com/icsharpcode/AvalonEdit
- 6.3.1.120, 2025-04-13, MIT. Targets net462, net6.0-windows and net8.0-windows. The repo has 2.1k stars and was pushed 2026-10-06, with no release in about 18 months. — [NuGet API](https://api.nuget.org/v3/registration5-semver1/avalonedit/index.json); [GitHub](https://github.com/icsharpcode/AvalonEdit)

**DiffPlex / DiffPlex.Wpf** — https://github.com/mmanela/diffplex
- `DiffPlex` 1.9.0 (2025-09-13) and `DiffPlex.Wpf` 1.9.1 (2025-10-30), Apache-2.0. DiffPlex.Wpf targets net46/48 and net6/8/9-windows. The repo has 1.3k stars and was pushed 2026-10-09. — [NuGet API](https://api.nuget.org/v3/registration5-semver1/diffplex.wpf/index.json); [GitHub](https://github.com/mmanela/diffplex)

**CommunityToolkit.Mvvm** — https://github.com/CommunityToolkit/dotnet
- 8.4.2, 2026-03-25, MIT. Targets netstandard2.0/2.1, net8.0 and net8.0-windows10.0.17763. The repo has 3.8k stars, is maintained by Microsoft, and is part of the .NET Foundation. — [NuGet API](https://api.nuget.org/v3/registration5-semver1/communitytoolkit.mvvm/index.json); [GitHub](https://github.com/CommunityToolkit/dotnet)

**H.NotifyIcon.Wpf** — https://github.com/HavenDV/H.NotifyIcon
- Latest stable 2.4.1 (2025-12-01, MIT) targets only net462 and net10.0-windows7.0. 2.3.2 targets net462, net8.0-windows and net9.0-windows. 2.5.0 betas/dev builds exist. The repo has 769 stars and 3 open issues. — [NuGet nuspec 2.4.1 / 2.3.2](https://api.nuget.org/v3-flatcontainer/h.notifyicon.wpf/index.json); [GitHub](https://github.com/HavenDV/H.NotifyIcon)

**Toast notifications**
- `Microsoft.Toolkit.Uwp.Notifications` 7.1.3 was last published 2022-11-01. Its source repo CommunityToolkit/WindowsCommunityToolkit is archived ("ARCHIVE - New Repo"). — [NuGet API](https://api.nuget.org/v3/registration5-semver1/microsoft.toolkit.uwp.notifications/index.json); [GitHub](https://github.com/CommunityToolkit/WindowsCommunityToolkit)
- Microsoft's notification overview lists `AppNotificationManager` (Windows App SDK) as the recommended API for WPF, WinForms and unpackaged Win32 apps. For unpackaged apps, `Register` makes the process the COM server and takes display name and icon from the shell. Subscribe to `NotificationInvoked` before `Register`. — [Microsoft Learn: AppNotificationManager.Register](https://learn.microsoft.com/pl-pl/windows/windows-app-sdk/api/winrt/microsoft.windows.appnotifications.appnotificationmanager.register); [Microsoft Learn: notifications overview](https://learn.microsoft.com/et-ee/windows/apps/develop/notifications/)
- AppNotificationManager depends on the Windows App SDK Singleton package. With `WindowsAppSDKSelfContained=true`, check `AppNotificationManager.IsSupported` at runtime. — [Microsoft Learn: deploy self-contained apps](https://learn.microsoft.com/en-us/WINDOWS/APPS/package-and-deploy/self-contained-deploy/deploy-self-contained-apps)
- Conflicting source: a CommunityToolkit MAUI docs change says AppNotificationManager "only works within a packaged app identity". — [CommunityToolkit docs PR #655](https://github.com/MicrosoftDocs/CommunityToolkit/pull/655/files)
- `Microsoft.WindowsAppSDK` is at stable 2.5.1, with 2.5.4-experimental (2026-09-29). The repo has 4.7k stars. — [NuGet API](https://api.nuget.org/v3-flatcontainer/microsoft.windowsappsdk/index.json)

**WebView2**
- `Microsoft.Web.WebView2` stable is 1.0.4258.31. Prerelease is 1.0.4312 (2026-10-12). — [NuGet API](https://api.nuget.org/v3-flatcontainer/microsoft.web.webview2/index.json)

### Inferences
- Suggested package set (.NET 8; ranges also valid on net10):
  - `WPF-UI` [4.3.0,5.0.0): Fluent window, navigation, theme (Mica/dark mode), buttons and cards.
  - `CommunityToolkit.Mvvm` [8.4.2,9.0.0): source-generated `[ObservableProperty]` and `[RelayCommand]`, plus messenger for agent events into the UI.
  - `AvalonEdit` [6.3.1.120,7.0.0): read-only code viewer with syntax highlighting for tool output and file previews, and the edit box for prompts or files.
  - `DiffPlex` [1.9.0,2.0.0) + `DiffPlex.Wpf` [1.9.1,2.0.0): side-by-side or inline diff of proposed edits before approval.
  - `Markdig` [1.4.0,2.0.0): parse the streamed markdown. Render with `MdXaml` 1.27.0 (stable) or a custom Markdig-to-FlowDocument renderer. Avoid `Markdig.Wpf`, which is unmaintained and has no net8 TFM, though netcoreapp3.1 assets would still load.
  - `H.NotifyIcon.Wpf` pinned to 2.3.2 on .NET 8, or 2.4.x after moving to net10: tray icon and context menu for overnight mode.
- Chat rendering trade-off: FlowDocument-based markdown (MdXaml) can be slow when re-rendered on every streamed token. Common practice is to throttle re-renders, or render finished blocks only and keep the in-progress block as plain text. This is engineering judgment, not sourced.
- Toasts: for a self-contained single-file exe, the simplest options are (a) `H.NotifyIcon` balloon tips, or (b) the archived `Microsoft.Toolkit.Uwp.Notifications` 7.1.3. Option (b) still works but needs TFM `net8.0-windows10.0.17763.0` and is unmaintained. Windows App SDK `AppNotificationManager` is the supported path, but it pulls in the WinAppSDK runtime (bootstrapper or self-contained mode). That adds size and complexity to a single-file exe, and the sources conflict on whether it works unpackaged. Prototype it before committing.

### Gaps
- No Microsoft Learn page was retrievable on the current status and TFM requirements of `Microsoft.Toolkit.Uwp.Notifications` for desktop C#: the send-local-toast URLs returned 404. A replacement package `CommunityToolkit.WinUI.Notifications` was not found on NuGet in this session.
- MdXaml streaming-render performance and Markdig.Wpf behaviour on .NET 8 were not benchmarked or documented in the sources read.
- Release notes for WPF-UI 4.x were not reviewed for breaking changes from 3.x.

## Q3: Compatibility with PublishSingleFile + self-contained (IncludeNativeLibrariesForSelfExtract), trimming, and native dependencies (WebView2 runtime on company laptops)

### Takeaway
Every managed library above bundles fine into a self-contained single-file exe. The native files (WPF's own native DLLs, `WebView2Loader.dll`) need `IncludeNativeLibrariesForSelfExtract=true`, which extracts them to `%TEMP%\.net` at startup. Trimming is not supported for WPF, so `PublishTrimmed` must stay off. WebView2 also needs the Evergreen runtime on the machine. It ships with Windows 11 and is on "the vast majority" of Windows 10 devices, but enterprise admins can block updates, so OrchDesk needs a detection and fallback path.

### Cited Findings
- Single-file bundles only managed DLLs by default. Native binaries stay separate unless `IncludeNativeLibrariesForSelfExtract=true`, which extracts them to disk at startup: to `%TEMP%/.net` on Windows, or to `DOTNET_BUNDLE_EXTRACT_BASE_DIR` if set. `IncludeAllContentForSelfExtract` is a .NET Core 3.1 compatibility mode, is not recommended, and might be removed. — [Microsoft Learn: single-file overview](https://learn.microsoft.com/en-us/dotnet/core/deploying/single-file/overview)
- In single-file apps, `Assembly.Location` returns an empty string, `Assembly.CodeBase` throws, and `Module.Name` returns `<Unknown>`. Use `AppContext.BaseDirectory` and `Environment.ProcessPath` instead. `EnableCompressionInSingleFile` reduces size at a startup cost. `DebugType=embedded` puts PDBs inside the bundle. — [Microsoft Learn: single-file overview](https://learn.microsoft.com/en-us/dotnet/core/deploying/single-file/overview)
- The .NET SDK disables trimming for WPF and Windows Forms because WPF relies heavily on reflection and almost no WPF apps run after trimming. — [Microsoft Learn: trimming incompatibilities](https://learn.microsoft.com/lt-lt/dotnet/core/deploying/trimming/incompatibilities); [dotnet/sdk#30948](https://github.com/dotnet/sdk/issues/30948)
- WebView2 apps must ship `WebView2Loader.dll`, which is native and per-architecture under `runtimes\win-*\native`, along with `Microsoft.Web.WebView2.Core.dll` and `Microsoft.Web.WebView2.WPF.dll`. Alternatively, the loader can be statically linked. — [Microsoft Learn: WebView2 distribution](https://learn.microsoft.com/en-us/microsoft-edge/webview2/concepts/distribution)
- For a single executable, Microsoft documents `<WebView2LoaderPreference>Static</WebView2LoaderPreference>`. The documented example is a C++ `.vcxproj`. — [Microsoft Learn: WebView2 single executable](https://learn.microsoft.com/en-us/microsoft-edge/webview2/how-to/static)
- The Evergreen WebView2 Runtime is part of Windows 11. "The vast majority of Windows 10 devices" have it, but "a small number" don't, so apps should detect it via the registry key `pv` under `...\EdgeUpdate\Clients\{F3017226-FE2A-4295-8BDF-00C3A9A7E4C5}` (HKLM WOW6432Node and HKCU) or via `GetAvailableCoreWebView2BrowserVersionString`. If it is missing, install with the bootstrapper (`MicrosoftEdgeWebview2Setup.exe /silent /install`) or the offline standalone installer. A non-elevated install is per-user. — [Microsoft Learn: WebView2 distribution](https://learn.microsoft.com/en-us/microsoft-edge/webview2/concepts/distribution)
- IT admins can disable WebView2 Runtime updates, so apps must feature-detect newer APIs. Fixed Version runtime is over 250 MB, can't run from a UNC or network path, and on Windows 10 for unpackaged apps (v120+) needs `icacls` grants for the App Container SIDs. A long-running app keeps the old runtime until it restarts (`NewBrowserVersionAvailable` event). — [Microsoft Learn: WebView2 distribution](https://learn.microsoft.com/en-us/microsoft-edge/webview2/concepts/distribution)
- The Microsoft.Web.WebView2 NuGet registration lists no SPDX licence expression. It uses a file licence under Microsoft terms. — [NuGet API](https://api.nuget.org/v3/registration5-semver1/microsoft.web.webview2/index.json)
- The Windows App SDK `AppNotificationManager` depends on the Singleton package. Self-contained WinAppSDK (`WindowsAppSDKSelfContained`) copies framework files next to the exe. — [Microsoft Learn: deploy self-contained apps](https://learn.microsoft.com/en-us/WINDOWS/APPS/package-and-deploy/self-contained-deploy/deploy-self-contained-apps)

### Inferences
- Per-library single-file status:
  - Pure managed, no issues expected: MEAI, OpenAI, Anthropic, OllamaSharp, ModelContextProtocol, Markdig, MdXaml, AvalonEdit, DiffPlex(.Wpf), CommunityToolkit.Mvvm, WPF-UI.
  - H.NotifyIcon: managed P/Invoke to shell32/user32, so it should bundle fine.
  - WebView2: needs the native loader. Use `IncludeNativeLibrariesForSelfExtract` or static loader, plus the runtime on the machine.
  - Windows App SDK: the hardest fit. It wants a bootstrapper or loose files.
- Set an explicit WebView2 `UserDataFolder` (for example `%LOCALAPPDATA%\OrchDesk\WebView2`) through `CoreWebView2Environment.CreateAsync`. Otherwise the default can resolve next to the exe or the extraction folder, which may be read-only or temporary. This is inference from common WebView2 single-file reports; no primary source was read here.
- Recommendation: make the WPF-native renderer (WPF-UI + MdXaml/Markdig + AvalonEdit + DiffPlex.Wpf) the default chat surface. Treat WebView2 as an optional "rich renderer" toggle, enabled only after runtime detection. This avoids a hard dependency on a runtime that locked-down company laptops may have disabled or outdated.
- Recommended publish properties: `PublishSingleFile=true`, `SelfContained=true`, `RuntimeIdentifier=win-x64`, `IncludeNativeLibrariesForSelfExtract=true`, `PublishTrimmed=false`, and optionally `EnableCompressionInSingleFile=true` and `DebugType=embedded`. Some corporate endpoint-protection (EDR/AppLocker) policies block execution from `%TEMP%`. If so, set `DOTNET_BUNDLE_EXTRACT_BASE_DIR` to a per-user AppData path. This last point is inference, not sourced.

### Gaps
- The exact list of WPF native runtime files extracted under `IncludeNativeLibrariesForSelfExtract` (wpfgfx, PresentationNative, D3DCompiler, vcruntime) was not confirmed from a source this session.
- Whether `WebView2LoaderPreference=Static` applies to .NET/WPF projects (as opposed to C++) was not confirmed. The fetched doc shows only `.vcxproj`.
- No source was found confirming whether Microsoft.Toolkit.Uwp.Notifications' COM activator works inside a single-file bundle.
