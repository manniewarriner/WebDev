using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OrchDesk.App.Services;

namespace OrchDesk.App.ViewModels;

public sealed partial class MainViewModel : ObservableObject
{
    public AppServices Services { get; }
    public ChatViewModel Chat { get; }
    public IReadOnlyList<ModelUsageRow> ModelUsageRows => ModelUsageRow.Read(Services.Usage);
    public AccountsViewModel Accounts { get; }

    [ObservableProperty] private string _page = "Chat";

    public MainViewModel(AppServices services, IFolderPicker? folderPicker = null)
    {
        Services = services;
        Chat = new ChatViewModel(services, folderPicker: folderPicker);
        Accounts = new AccountsViewModel(services);
    }

    [ObservableProperty] private string _connectionStatus = "Offline";
    public string SidebarStatus => ConnectionStatus == "PC online" ? "Ready \u00b7 PC models online" : ConnectionStatus == "Cloud only" ? "Ready \u00b7 cloud only" : "Offline";
    partial void OnConnectionStatusChanged(string value) => OnPropertyChanged(nameof(SidebarStatus));

    public bool IsDark => Services.Config.Theme != "Light";

    [RelayCommand]
    private void ToggleTheme()
    {
        Services.Config.Theme = IsDark ? "Light" : "Dark";
        App.ApplyTheme(Services.Config.Theme);
        if (!Services.IsScreenshot) Services.Store.Save(Services.Config);
        OnPropertyChanged(nameof(IsDark));
    }

    [RelayCommand]
    private void OpenSettings()
    {
        var window = new Views.SettingsWindow(new SettingsViewModel(Services)) { Owner = Application.Current.MainWindow };
        window.ShowDialog();
    }
}
