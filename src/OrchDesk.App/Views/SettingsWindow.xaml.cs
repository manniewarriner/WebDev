using System.Windows;
using System.Windows.Controls;
using OrchDesk.App.ViewModels;
namespace OrchDesk.App.Views;
public partial class SettingsWindow : Window
{
    private readonly SettingsViewModel _vm;
    public SettingsWindow(SettingsViewModel vm) { InitializeComponent(); DataContext = _vm = vm; Loaded += (_, _) => { if (Owner is { } owner) { Width = Math.Max(MinWidth, owner.ActualWidth); Height = Math.Max(MinHeight, owner.ActualHeight); } }; vm.Saved += () => { }; }
    public void SelectPage(string page) => SettingsTabs.SelectedIndex = page switch { "settings-appearance" => 1, "settings-about" => 5, "settings-fallback" => 2, "settings-overnight" => 3, "settings-limits" => 4, _ => 0 };
    private void ConnectProvider(object sender, RoutedEventArgs e) { if (sender is Button { Tag: ProviderEdit provider }) new ConnectProviderWindow(_vm, provider) { Owner = this }.ShowDialog(); }
    private void ChangePicture(object sender,RoutedEventArgs e) => OrchDesk.App.Services.ProfilePicture.Pick(_vm.Services,this);
    private async void CheckUpdates(object sender,RoutedEventArgs e) { UpdateButton.IsEnabled=false; try { await OrchDesk.App.Services.UpdateService.CheckAsync(this,_vm.Services.IsScreenshot); } finally { UpdateButton.IsEnabled=true; } }
    private void ApiKey_Changed(object sender, RoutedEventArgs e) { if (sender is PasswordBox { DataContext: ProviderEdit p } box) _vm.StoreApiKey(p, box.Password); }
    private void CfId_Changed(object sender, RoutedEventArgs e) { if (sender is PasswordBox { DataContext: ProviderEdit p } box) p.NewAccessClientId = box.Password; }
    private void CfSecret_Changed(object sender, RoutedEventArgs e) { if (sender is PasswordBox { DataContext: ProviderEdit p } box) p.NewAccessClientSecret = box.Password; }
    private void Meshy_Changed(object sender, RoutedEventArgs e) { if (sender is PasswordBox box) _vm.NewMeshyKey = box.Password; }
}
