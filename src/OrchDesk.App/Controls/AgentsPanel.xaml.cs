using System.Windows;
using System.Windows.Controls;
using OrchDesk.App.ViewModels;
namespace OrchDesk.App.Controls;
public partial class AgentsPanel : UserControl
{
    public event Action<AgentRow>? Selected;
    public event Action? Usage;
    public AgentsPanel() => InitializeComponent();
    private void RowClick(object sender, RoutedEventArgs e) { if (sender is FrameworkElement { DataContext: AgentRow row }) Selected?.Invoke(row); }
    private void UsageClick(object sender, RoutedEventArgs e) => Usage?.Invoke();
}
