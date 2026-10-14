using System.Windows;
using OrchDesk.App.Services;
using OrchDesk.App.ViewModels;

namespace OrchDesk.App;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
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
