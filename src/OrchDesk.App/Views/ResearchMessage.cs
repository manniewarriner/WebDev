using CommunityToolkit.Mvvm.ComponentModel;

namespace OrchDesk.App.Views;

public sealed partial class ResearchMessage : ObservableObject
{
    public string Role { get; init; } = "";
    public bool IsUser => Role == "you";
    [ObservableProperty] private string text = "";
}
