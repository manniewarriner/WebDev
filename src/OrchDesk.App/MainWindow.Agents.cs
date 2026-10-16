using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using OrchDesk.App.ViewModels;
using OrchDesk.Core.Presentation;
namespace OrchDesk.App;
public partial class MainWindow
{
    private AgentsPanelState _agentsState = new();
    private void InitializeAgents()
    {
        SizeChanged += (_, _) => LayoutAgents();
        DataContextChanged += (_, _) => { if (DataContext is MainViewModel vm) { _agentsState = new(vm.Services.Config.AgentsPanelOpen); LayoutAgents(); } };
        LiveAgents.Usage += () => { if (DataContext is MainViewModel vm) ShowModelUsage(ModelUsageRow.Read(vm.Services.Usage)); };
        LiveAgents.Selected += row => {
            foreach (var element in AgentDescendants(ChatScroll).OfType<FrameworkElement>().Where(e => ReferenceEquals(e.DataContext, row.Card))) {
                if (element is Expander expander) expander.IsExpanded = true;
            }
            var target = AgentDescendants(ChatScroll).OfType<Border>().FirstOrDefault(e => ReferenceEquals(e.DataContext, row.Card));
            target?.BringIntoView();
        };
    }
    private static IEnumerable<DependencyObject> AgentDescendants(DependencyObject parent)
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++) { var child = VisualTreeHelper.GetChild(parent, i); yield return child; foreach (var nested in AgentDescendants(child)) yield return nested; }
    }
    private void ToggleAgents(object sender, RoutedEventArgs e)
    {
        _agentsState.Toggle();
        if (DataContext is MainViewModel vm) { vm.Services.Config.AgentsPanelOpen = _agentsState.Preference; vm.Services.Save(); }
        LayoutAgents();
    }
    private void LayoutAgents()
    {
        _agentsState.Resize(ActualWidth);
        var open = _agentsState.IsOpen;
        Sidebar.Visibility = _agentsState.IsDrawer ? Visibility.Collapsed : Visibility.Visible;
        AgentsColumn.Width = new GridLength(open && !_agentsState.IsDrawer ? 320 : 0);
        LiveAgents.Visibility = open ? Visibility.Visible : Visibility.Collapsed;
        Grid.SetColumn(LiveAgents, _agentsState.IsDrawer ? 0 : 1);
        LiveAgents.VerticalAlignment = _agentsState.IsDrawer ? VerticalAlignment.Bottom : VerticalAlignment.Stretch;
        LiveAgents.MaxHeight = _agentsState.IsDrawer ? 440 : double.PositiveInfinity;
        AgentsAction.ToolTip = open ? "Collapse agents" : "Show agents";
    }
}
