using System.Windows;
using System.Windows.Controls;
using OrchDesk.App.ViewModels;
namespace OrchDesk.App.Views;
public partial class SettingsWindow : Window
{
    private readonly SettingsViewModel _vm;
    public SettingsWindow(SettingsViewModel vm) { InitializeComponent(); DataContext = _vm = vm; vm.Saved += () => { }; }
    private void ChangePicture(object sender,RoutedEventArgs e) => OrchDesk.App.Services.ProfilePicture.Pick(_vm.Services,this);
    private async void CheckUpdates(object sender,RoutedEventArgs e) { UpdateButton.IsEnabled=false; try { await OrchDesk.App.Services.UpdateService.CheckAsync(this,_vm.Services.IsScreenshot); } finally { UpdateButton.IsEnabled=true; } }
    private void ApiKey_Changed(object sender, RoutedEventArgs e) { if (sender is PasswordBox { DataContext: ProviderEdit p } box) _vm.StoreApiKey(p, box.Password); }
    private void CfId_Changed(object sender, RoutedEventArgs e) { if (sender is PasswordBox { DataContext: ProviderEdit p } box) p.NewAccessClientId = box.Password; }
    private void CfSecret_Changed(object sender, RoutedEventArgs e) { if (sender is PasswordBox { DataContext: ProviderEdit p } box) p.NewAccessClientSecret = box.Password; }
    private void Meshy_Changed(object sender, RoutedEventArgs e) { if (sender is PasswordBox box) _vm.NewMeshyKey = box.Password; }
}
