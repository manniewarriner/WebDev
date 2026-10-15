using System.Windows;
using OrchDesk.App.Services;
using OrchDesk.App.ViewModels;

namespace OrchDesk.App;

public partial class App : Application
{
    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        OrchDesk.Core.Config.ConfigStore storeOverride;
        try { storeOverride = OrchDesk.Core.Config.ConfigStore.FromArgs(e.Args); } catch (ArgumentException) { Shutdown(e.Args.Contains("--send", StringComparer.OrdinalIgnoreCase) ? 1 : 2); return; }
        var sdkFolder = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".dotnet");
        if (System.IO.File.Exists(System.IO.Path.Combine(sdkFolder, "dotnet.exe"))) Environment.SetEnvironmentVariable("PATH", sdkFolder + System.IO.Path.PathSeparator + Environment.GetEnvironmentVariable("PATH"));


        if (e.Args.Contains("--send", StringComparer.OrdinalIgnoreCase))
        {
            ShutdownMode = ShutdownMode.OnExplicitShutdown;
            var exit = 1;
            try
            {
                System.IO.Directory.CreateDirectory(storeOverride.Directory);
                await System.IO.File.WriteAllTextAsync(System.IO.Path.Combine(storeOverride.Directory, "send-result.txt"), "");
                var options = OrchDesk.Core.Config.SendOptions.Parse(e.Args)!;
                var sendServices = new AppServices(store: storeOverride, chatOnly: true);
                var chat = new ChatViewModel(sendServices);
                if (options.ProviderId is { } id)
                {
                    chat.SelectedProvider = chat.ProviderOptions.FirstOrDefault(p => p.Id == id)
                        ?? new OrchDesk.Core.Config.ModelPickerOption(id, id);
                }
                chat.SelectedProvider = options.ProviderId is null ? chat.ProviderOptions[0] : chat.SelectedProvider;
                var results = new List<object>();
                exit = 0;
                foreach (var text in options.Texts)
                {
                    var watch = System.Diagnostics.Stopwatch.StartNew();
                    var before = chat.Items.Count;
                    chat.Input = text;
                    if (chat.SendCommand.CanExecute(null)) await chat.SendCommand.ExecuteAsync(null);
                    var added = chat.Items.Skip(before).ToList();
                    var reply = string.Concat(added.OfType<AssistantItem>().Select(a => a.Text));
                    var error = added.OfType<ErrorItem>().Any() ? "Send failed: providers unavailable; see attempt reasons." : "";
                    var provider = chat.CurrentConversation?.LastProvider;
                    if (error.Length == 0 && string.IsNullOrWhiteSpace(reply)) error = "Send failed: provider returned an empty reply.";
                    var output = error.Length > 0 ? error : $"[{provider?.Id}/{provider?.ModelId}] " + reply;
                    if (error.Length > 0 || string.IsNullOrWhiteSpace(reply)) { exit = 1; Console.Error.WriteLine(output); }
                    Console.WriteLine(output);
                    await System.IO.File.AppendAllTextAsync(System.IO.Path.Combine(storeOverride.Directory, "send-result.txt"), output + Environment.NewLine);
                    results.Add(new { provider = provider?.Id, model = provider?.ModelId, reply, error, elapsedMs = watch.ElapsedMilliseconds, attempts = chat.CurrentConversation?.Attempts.Select(a => new { a.ProviderId, a.Model, a.Reason }).ToArray(), diagnostics = options.Diag ? chat.CurrentConversation?.Attempts.ToArray() : null });
                    if (exit != 0) break;
                }
                await System.IO.File.WriteAllTextAsync(System.IO.Path.Combine(storeOverride.Directory, "send-result.json"), System.Text.Json.JsonSerializer.Serialize(results, OrchDesk.Core.Config.ConfigStore.Json));
            }
            catch (Exception ex)
            {
                exit = 1;
                var error = ex is ArgumentException ? ex.Message : "Send failed: provider or configuration unavailable.";
                Console.Error.WriteLine(error); Console.WriteLine(error);
            }
            Shutdown(exit);
            return;
        }

        if (e.Args.Contains("--overnight-goal", StringComparer.OrdinalIgnoreCase))
        {
            ShutdownMode = ShutdownMode.OnExplicitShutdown;
            string? Arg(string name) { var i = Array.FindIndex(e.Args, a => a.Equals(name, StringComparison.OrdinalIgnoreCase)); return i >= 0 && i + 1 < e.Args.Length && !e.Args[i + 1].StartsWith("--") ? e.Args[i + 1] : null; }
            try {
                var goal = Arg("--overnight-goal") ?? throw new ArgumentException("Goal required");
                var workspace = System.IO.Path.GetFullPath(Arg("--workspace") ?? throw new ArgumentException("Workspace required"));
                if (!System.IO.Directory.Exists(workspace)) throw new ArgumentException("Workspace missing");
                var until = TimeOnly.ParseExact(Arg("--until") ?? "08:00", "HH:mm", System.Globalization.CultureInfo.InvariantCulture);
                var deadline = DateTime.Today.Add(until.ToTimeSpan()); if (deadline <= DateTime.Now) deadline = deadline.AddDays(1);
                var store = storeOverride; var config = store.Load();
                using var http = new System.Net.Http.HttpClient { Timeout = Timeout.InfiniteTimeSpan };
                var providers = new OrchDesk.Core.Config.ProviderFactory(new OrchDesk.Core.Config.DpapiSecretProtector(), http).CreateAll(config);
                var run = new OrchDesk.Core.Overnight.OvernightRun {
                    Goal = goal, Workspace = workspace, Deadline = new DateTimeOffset(deadline),
                    ManagerId = Arg("--manager") ?? config.Supervisor.PlannerProviderId,
                    WorkerIds = (Arg("--workers") ?? config.Supervisor.WorkerProviderId).Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries).ToList(),
                    MaxRounds = Math.Clamp(int.Parse(Arg("--max-rounds") ?? config.Supervisor.MaxRounds.ToString()), 1, 100),
                    MaxSteps = config.MaxSteps, TestCommand = config.Supervisor.TestCommand,
                    ShutdownWhenDone = e.Args.Contains("--shutdown-when-done", StringComparer.OrdinalIgnoreCase)
                };
                var policy = new OrchDesk.Core.Permissions.PermissionPolicy(); policy.ExtraAllowedCommandPrefixes.AddRange(config.ExtraAllowedCommands);
                var runner = new OrchDesk.Core.Overnight.OvernightRoundRunner(System.IO.Path.Combine(store.Directory, "runs"), OrchDesk.Core.Tools.ToolRegistry.CreateDefault(config.WebFetchEnabled), policy);
                await runner.RunAsync(run, providers, CancellationToken.None);
                Shutdown(run.Status == "Completed" ? 0 : 1);
            } catch { Shutdown(2); }
            return;
        }

        if (e.Args.Contains("--smoketest", StringComparer.OrdinalIgnoreCase))
        {
            ShutdownMode = ShutdownMode.OnExplicitShutdown;
            var store = storeOverride;
            using var http = new System.Net.Http.HttpClient { Timeout = Timeout.InfiniteTimeSpan };
            try {
                var results = await OrchDesk.Core.Verification.IntegrationSmokeRunner.RunAsync(store.Load(), new OrchDesk.Core.Config.DpapiSecretProtector(), http, System.IO.Path.Combine(store.Directory, "smoketest"), saveSynced: store.Save);
                Shutdown(results.Any(r => r.Status == "fail") ? 1 : 0);
            } catch { Shutdown(2); }
            return;
        }

        if (e.Args.Contains("--selftest", StringComparer.OrdinalIgnoreCase))
        {
            ShutdownMode = ShutdownMode.OnExplicitShutdown;
            var index = Array.FindIndex(e.Args, a => a.Equals("--selftest", StringComparison.OrdinalIgnoreCase));
            if (index + 1 >= e.Args.Length || e.Args[index + 1].StartsWith("--")) { Shutdown(2); return; }
            var folder = e.Args[index + 1];
            var providerIndex = Array.FindIndex(e.Args, a => a.Equals("--provider", StringComparison.OrdinalIgnoreCase));
            var id = providerIndex < 0 ? "local-pc" : providerIndex + 1 < e.Args.Length ? e.Args[providerIndex + 1] : "";
            try {
                using var http = new System.Net.Http.HttpClient { Timeout = TimeSpan.FromMinutes(3) };
                var config = storeOverride.Load();
                // local-pc is the explicit LM Studio verification target, independent of older saved defaults.
                var providerConfig = id == "local-pc" && !e.Args.Contains("--config", StringComparer.OrdinalIgnoreCase) ? OrchDesk.Core.Config.AppConfig.CreateDefault().Providers.First(p => p.Id == id)
                    : config.Providers.FirstOrDefault(p => p.Id == id && p.Kind == OrchDesk.Core.Config.ProviderKind.OpenAiCompatible);
                var provider = providerConfig is null ? null : new OrchDesk.Core.Config.ProviderFactory(new OrchDesk.Core.Config.DpapiSecretProtector(), http).Create(providerConfig);
                if (provider is null) {
                    System.IO.Directory.CreateDirectory(folder);
                    await System.IO.File.WriteAllTextAsync(System.IO.Path.Combine(folder, "selftest.json"), "{\"passed\":false,\"items\":[{\"name\":\"provider\",\"pass\":false,\"detail\":\"Unknown or disabled OpenAI-compatible provider\"}]}");
                    Shutdown(2); return;
                }
                var report = await OrchDesk.Core.Verification.SelfTestRunner.RunAsync(folder, provider, accounts: new OrchDesk.Core.Config.ProviderFactory(new OrchDesk.Core.Config.DpapiSecretProtector(), http).CreateAll(config));
                Shutdown(report.Passed ? 0 : 1);
            } catch {
                // Do not display exception text: HTTP/provider diagnostics can contain secrets.
                try {
                    System.IO.Directory.CreateDirectory(folder);
                    await System.IO.File.WriteAllTextAsync(System.IO.Path.Combine(folder, "selftest.json"), "{\"passed\":false,\"items\":[{\"name\":\"setup\",\"pass\":false,\"detail\":\"Selftest setup failed\"}]}");
                } catch { }
                Shutdown(2);
            }
            return;
        }
        DispatcherUnhandledException += (_, args) =>
        {
            MessageBox.Show(args.Exception.Message, "OrchDesk hit an error", MessageBoxButton.OK, MessageBoxImage.Warning);
            args.Handled = true;
        };

        var screenshotMode = e.Args.Contains("--screenshot", StringComparer.OrdinalIgnoreCase);
        var pageIndex = Array.FindIndex(e.Args, a => a.Equals("--page", StringComparison.OrdinalIgnoreCase));
        var page = pageIndex >= 0 && pageIndex + 1 < e.Args.Length ? e.Args[pageIndex + 1].ToLowerInvariant() : null;
        if (pageIndex >= 0 && page is not ("accounts" or "settings-appearance" or "settings-about" or "heatmap" or "welcome" or "filters" or "background" or "integrations" or "picker" or "thread" or "thinking" or "reply" or "selector" or "composer" or "composer-empty" or "composer-ready" or "sidebar-collapsed" or "sidebar-expanded" or "home" or "palette" or "connect-dialog" or "settings-fallback" or "settings-overnight" or "settings-limits" or "overnight-setup" or "toasts" or "usage" or "file-tree" or "diff" or "code-block" or "skeleton")) { Shutdown(2); return; }
        Motion.Screenshot = screenshotMode;
        var services = new AppServices(screenshotMode, storeOverride, OrchDesk.Core.Config.ScreenshotOptions.UseDemo(e.Args), chatOnly: e.Args.Contains("--ui-selftest", StringComparer.OrdinalIgnoreCase));
        if (screenshotMode && !services.UseDemo) await services.StartupSync;
        var themeIndex = Array.FindIndex(e.Args, a => a.Equals("--theme", StringComparison.OrdinalIgnoreCase));
        if (themeIndex >= 0 && themeIndex + 1 < e.Args.Length) services.Config.Theme = e.Args[themeIndex + 1];
        ApplyTheme(services.Config.Theme);
        var uiSelftest = e.Args.Contains("--ui-selftest", StringComparer.OrdinalIgnoreCase);
        var folderIndex = Array.FindIndex(e.Args, a => a.Equals("--folder", StringComparison.OrdinalIgnoreCase));
        var suppliedFolder = folderIndex >= 0 && folderIndex + 1 < e.Args.Length ? e.Args[folderIndex + 1] : null;
        var vm = new MainViewModel(services, uiSelftest && suppliedFolder is not null ? new SuppliedFolderPicker(suppliedFolder) : null);
        var window = new MainWindow { DataContext = vm };
        MainWindow = window;
        window.Show();
        await window.Dispatcher.InvokeAsync(() => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
        if (uiSelftest) {
            ShutdownMode = ShutdownMode.OnExplicitShutdown;
            var passed = await window.RunUiSelftestAsync(storeOverride.Directory, suppliedFolder);
            window.Close(); Shutdown(passed ? 0 : 1); return;
        }
        Window captureWindow = window;
        if (page == "integrations" || page?.StartsWith("settings-") == true)
        {
            captureWindow = new Views.SettingsWindow(new SettingsViewModel(services)) { Owner = window };
            ((Views.SettingsWindow)captureWindow).SelectPage(page!);
            captureWindow.Show();
        }
        else if (screenshotMode) window.PrepareScreenshot(page);

        var screenshotIndex = Array.FindIndex(e.Args, a => a.Equals("--screenshot", StringComparison.OrdinalIgnoreCase));
        if (screenshotIndex >= 0)
        {
            if (screenshotIndex + 1 >= e.Args.Length || e.Args[screenshotIndex + 1].StartsWith("--")) { Shutdown(2); return; }
            try
            {
                await captureWindow.Dispatcher.InvokeAsync(() => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
                var widthIndex = Array.FindIndex(e.Args, a => a.Equals("--width", StringComparison.OrdinalIgnoreCase));
                if (widthIndex >= 0)
                {
                    if (widthIndex + 1 >= e.Args.Length || !int.TryParse(e.Args[widthIndex + 1], out var width) || width < captureWindow.MinWidth || width > 4096) { Shutdown(2); return; }
                    captureWindow.Width = width + captureWindow.ActualWidth - ((FrameworkElement)captureWindow.Content).ActualWidth;
                    await captureWindow.Dispatcher.InvokeAsync(() => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
                }
                var heightIndex = Array.FindIndex(e.Args, a => a.Equals("--height", StringComparison.OrdinalIgnoreCase));
                if (heightIndex >= 0 && heightIndex + 1 < e.Args.Length && int.TryParse(e.Args[heightIndex + 1], out var height)) captureWindow.Height = Math.Max(captureWindow.MinHeight, height);
                await captureWindow.Dispatcher.InvokeAsync(() => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
                captureWindow.UpdateLayout();
                var captureRoot = captureWindow.Content as FrameworkElement ?? captureWindow;
                var dpi = widthIndex >= 0 ? new DpiScale(1, 1) : System.Windows.Media.VisualTreeHelper.GetDpi(captureRoot);
                var bitmap = new System.Windows.Media.Imaging.RenderTargetBitmap(
                    (int)Math.Ceiling(captureRoot.ActualWidth * dpi.DpiScaleX), (int)Math.Ceiling(captureRoot.ActualHeight * dpi.DpiScaleY),
                    dpi.PixelsPerInchX, dpi.PixelsPerInchY, System.Windows.Media.PixelFormats.Pbgra32);
                // Native frame pixels are not WPF visuals. Fill the canvas first so the PNG
                // stays opaque and uses the same theme background around the client area.
                var canvas = new System.Windows.Media.DrawingVisual();
                using (var drawing = canvas.RenderOpen())
                    drawing.DrawRectangle(captureWindow.Background, null, new Rect(0, 0, captureRoot.ActualWidth, captureRoot.ActualHeight));
                bitmap.Render(canvas);
                bitmap.Render(captureRoot);
                var encoder = new System.Windows.Media.Imaging.PngBitmapEncoder();
                encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bitmap));
                var path = System.IO.Path.GetFullPath(e.Args[screenshotIndex + 1]);
                System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path)!);
                using var stream = System.IO.File.Create(path);
                encoder.Save(stream);
                Shutdown(0);
            }
            catch { Shutdown(2); }
            return;
        }

        _ = vm.Chat.ResumeOvernightAsync();


    }

    public static void ApplyTheme(string theme)
    {
        var dicts = Current.Resources.MergedDictionaries;
        var uri = new Uri(theme == "Light" ? "Themes/Light.xaml" : "Themes/Dark.xaml", UriKind.Relative);
        dicts[0] = new ResourceDictionary { Source = uri };
        Motion.Refresh();
    }
}
