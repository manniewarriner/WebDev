using System.Windows;
using System.Windows.Controls;
using OrchDesk.App.ViewModels;
namespace OrchDesk.App.Views;
public partial class SettingsWindow : Window
{
    private readonly SettingsViewModel _vm;
    public SettingsWindow(SettingsViewModel vm) { InitializeComponent(); DataContext = _vm = vm; vm.Saved += () => { }; }
    private void ApiKey_Changed(object sender, RoutedEventArgs e) { if (sender is PasswordBox { DataContext: ProviderEdit p } box) p.NewApiKey = box.Password; }
    private void CfId_Changed(object sender, RoutedEventArgs e) { if (sender is PasswordBox { DataContext: ProviderEdit p } box) p.NewAccessClientId = box.Password; }
    private void CfSecret_Changed(object sender, RoutedEventArgs e) { if (sender is PasswordBox { DataContext: ProviderEdit p } box) p.NewAccessClientSecret = box.Password; }
    private void Meshy_Changed(object sender, RoutedEventArgs e) { if (sender is PasswordBox box) _vm.NewMeshyKey = box.Password; }
}
