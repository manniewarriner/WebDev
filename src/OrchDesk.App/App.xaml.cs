using System.Windows;
using OrchDesk.App.Services;
using OrchDesk.App.ViewModels;

namespace OrchDesk.App;

public partial class App : Application
{
    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);


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
                var config = new OrchDesk.Core.Config.ConfigStore().Load();
                // local-pc is the explicit LM Studio verification target, independent of older saved defaults.
                var providerConfig = id == "local-pc" ? OrchDesk.Core.Config.AppConfig.CreateDefault().Providers.First(p => p.Id == id)
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

        var services = new AppServices();
        ApplyTheme(services.Config.Theme);
        var vm = new MainViewModel(services);
        var window = new MainWindow { DataContext = vm };
        MainWindow = window;
        window.Show();

        // Started by the nightly scheduled task: open on the Overnight page and work through the queue.
        if (e.Args.Contains("--overnight", StringComparer.OrdinalIgnoreCase))
        {
            vm.Page = "Overnight";
            _ = vm.Overnight.RunNowAsync();
        }
    }

    public static void ApplyTheme(string theme)
    {
        var dicts = Current.Resources.MergedDictionaries;
        var uri = new Uri(theme == "Light" ? "Themes/Light.xaml" : "Themes/Dark.xaml", UriKind.Relative);
        dicts[0] = new ResourceDictionary { Source = uri };
    }
}
