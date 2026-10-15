using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using OrchDesk.App.Services;
using OrchDesk.App.ViewModels;

namespace OrchDesk.App.Views;

/// <summary>Edits the same provider object as the integrations card; the existing sync/auth pipeline owns persistence.</summary>
public sealed class ConnectProviderWindow : Window
{
    public ConnectProviderWindow(SettingsViewModel vm, ProviderEdit provider)
    {
        Title = "Connect " + provider.FriendlyName; Width = 540; SizeToContent = SizeToContent.Height; MaxHeight = 720; WindowStartupLocation = WindowStartupLocation.CenterOwner; DataContext = provider;
        SetResourceReference(BackgroundProperty, "BaseBrush"); SetResourceReference(ForegroundProperty, "TextBrush"); SetResourceReference(FontFamilyProperty, "UiFont");
        var body = new StackPanel { Margin = new Thickness(32) }; Content = body;
        body.Children.Add(new TextBlock { Text = provider.FriendlyName, Style = (Style)FindResource("Heading"), Margin = new Thickness(0, 0, 0, 8) });
        body.Children.Add(new TextBlock { Text = provider.Subtitle, Style = (Style)FindResource("Muted"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 24) });
        void Field(string label, string path) { body.Children.Add(new TextBlock { Text = label, Style = (Style)FindResource("FieldLabel") }); var input = new TextBox(); input.SetBinding(TextBox.TextProperty, new Binding(path) { Mode = BindingMode.TwoWay, UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged }); body.Children.Add(input); }
        if (provider.IsHttp) { Field("Endpoint", nameof(ProviderEdit.BaseUrl)); body.Children.Add(new TextBlock { Text = "API key", Style = (Style)FindResource("FieldLabel") }); var key = new PasswordBox(); key.PasswordChanged += (_, _) => vm.StoreApiKey(provider, key.Password); body.Children.Add(key); }
        if (provider.IsClaude) Field("Subscription profile folder", nameof(ProviderEdit.ClaudeConfigDir));
        var enabled = new CheckBox { Content = "Enabled", Margin = new Thickness(0, 16, 0, 16) }; enabled.SetBinding(CheckBox.IsCheckedProperty, new Binding(nameof(ProviderEdit.Enabled)) { Mode = BindingMode.TwoWay }); body.Children.Add(enabled);
        var status = new TextBlock { Style = (Style)FindResource("Muted"), TextWrapping = TextWrapping.Wrap }; status.SetBinding(TextBlock.TextProperty, new Binding(nameof(ProviderEdit.Status))); body.Children.Add(status);
        var actions = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 24, 0, 0) }; body.Children.Add(actions);
        var connect = new Button { Content = "Connect & sync", Style = (Style)FindResource("AccentButton") }; var test = new Button { Content = "Test", Margin = new Thickness(8, 0, 8, 0) }; var close = new Button { Content = "Done", IsCancel = true };
        connect.Click += async (_, _) => { if (vm.Services.IsScreenshot) return; connect.IsEnabled = test.IsEnabled = false; try { await vm.ConnectIntegrationCommand.ExecuteAsync(provider); } finally { connect.IsEnabled = test.IsEnabled = true; } };
        test.Click += async (_, _) => { if (vm.Services.IsScreenshot) return; connect.IsEnabled = test.IsEnabled = false; try { await vm.TestIntegrationCommand.ExecuteAsync(provider); } finally { connect.IsEnabled = test.IsEnabled = true; } };
        close.Click += (_, _) => Close(); actions.Children.Add(connect); actions.Children.Add(test); actions.Children.Add(close);
        Loaded += (_, _) => Motion.Reveal(body);
    }
}
