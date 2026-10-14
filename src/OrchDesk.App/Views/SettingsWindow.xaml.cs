using System.Windows;
using OrchDesk.App.ViewModels;

namespace OrchDesk.App.Views;

public partial class SettingsWindow : Window
{
    private readonly SettingsViewModel _vm;

    public SettingsWindow(SettingsViewModel vm)
    {
        InitializeComponent();
        DataContext = _vm = vm;
        vm.Saved += () => { DialogResult = true; };
        // PasswordBox can't data-bind (by design, so secrets don't sit in bindable strings); clear it when switching model.
        vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName != nameof(SettingsViewModel.Selected)) return;
            ApiKeyBox.Clear();
            CfIdBox.Clear();
            CfSecretBox.Clear();
        };
    }

    private void ApiKey_Changed(object sender, RoutedEventArgs e) { if (_vm.Selected is { } p) p.NewApiKey = ApiKeyBox.Password; }
    private void CfId_Changed(object sender, RoutedEventArgs e) { if (_vm.Selected is { } p) p.NewAccessClientId = CfIdBox.Password; }
    private void CfSecret_Changed(object sender, RoutedEventArgs e) { if (_vm.Selected is { } p) p.NewAccessClientSecret = CfSecretBox.Password; }
}
