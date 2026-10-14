using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OrchDesk.App.Services;

namespace OrchDesk.App.ViewModels;

public sealed partial class MainViewModel : ObservableObject
{
    public AppServices Services { get; }
    public ChatViewModel Chat { get; }
    public AccountsViewModel Accounts { get; }

    [ObservableProperty] private string _page = "Chat";

    public MainViewModel(AppServices services)
    {
        Services = services;
        Chat = new ChatViewModel(services);
        Accounts = new AccountsViewModel(services);
    }

    public bool IsDark => Services.Config.Theme != "Light";

    [RelayCommand]
    private void ToggleTheme()
    {
        Services.Config.Theme = IsDark ? "Light" : "Dark";
        App.ApplyTheme(Services.Config.Theme);
        Services.Store.Save(Services.Config);
        OnPropertyChanged(nameof(IsDark));
    }

    [RelayCommand]
    private void OpenSettings()
    {
        var window = new Views.SettingsWindow(new SettingsViewModel(Services)) { Owner = Application.Current.MainWindow };
        window.ShowDialog();
    }
}
