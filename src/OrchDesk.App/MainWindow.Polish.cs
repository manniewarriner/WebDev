using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using OrchDesk.App.ViewModels;

namespace OrchDesk.App;
public partial class MainWindow
{
    private void OpenTeamModelUsage(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: TeamItem team }) ShowModelUsage(team.ModelUsageRows);
    }
    private void ShowModelUsage(IReadOnlyList<ModelUsageRow> rows)
    {
        var body = ShowModal("Usage by model", 920);
        body.Children.Add(Label("Observed requests and tokens / Today (UTC) / this session", "Muted", 13));
        var table = new DataGrid { ItemsSource = rows, AutoGenerateColumns = false, IsReadOnly = true,
            CanUserAddRows = false, CanUserDeleteRows = false, HeadersVisibility = DataGridHeadersVisibility.Column,
            GridLinesVisibility = DataGridGridLinesVisibility.Horizontal, RowHeight = 36, MinHeight = 100,
            Margin = new Thickness(0, 16, 0, 0), BorderThickness = new Thickness(0) };
        table.SetResourceReference(Control.BackgroundProperty, "SurfaceBrush");
        table.SetResourceReference(Control.ForegroundProperty, "TextBrush");
        var rowStyle = new Style(typeof(DataGridRow));
        rowStyle.Setters.Add(new Setter(Control.BackgroundProperty, Brush("Surface")));
        rowStyle.Setters.Add(new Setter(Control.ForegroundProperty, Brush("Text")));
        table.RowStyle = rowStyle;
        var headerStyle = new Style(typeof(System.Windows.Controls.Primitives.DataGridColumnHeader));
        headerStyle.Setters.Add(new Setter(Control.BackgroundProperty, Brush("Raised")));
        headerStyle.Setters.Add(new Setter(Control.ForegroundProperty, Brush("Text")));
        headerStyle.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(8)));
        table.ColumnHeaderStyle = headerStyle;
        table.HorizontalGridLinesBrush = Brush("Border");
        var cellStyle = new Style(typeof(DataGridCell));
        cellStyle.Setters.Add(new Setter(Control.BorderThicknessProperty, new Thickness(0)));
        cellStyle.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(8, 4, 8, 4)));
        table.CellStyle = cellStyle;
        var textStyle = new Style(typeof(TextBlock));
        textStyle.Setters.Add(new Setter(TextBlock.TextTrimmingProperty, TextTrimming.CharacterEllipsis));
        textStyle.Setters.Add(new Setter(FrameworkElement.ToolTipProperty, new Binding("Model")));
        textStyle.Setters.Add(new Setter(FrameworkElement.MarginProperty, new Thickness(8, 4, 8, 4)));
        foreach (var (title, property, width) in new[] {
            ("Model", "Model", 260d), ("Today req.", "TodayRequests", 110d),
            ("Today tokens", "TodayTokens", 110d), ("Session req.", "SessionRequests", 120d),
            ("Session tok.", "SessionTokens", 110d), ("Backoff", "Backoff", 110d) })
            table.Columns.Add(new DataGridTextColumn { Header = title, Binding = new Binding(property), Width = width, ElementStyle = textStyle });
        body.Children.Add(table);
        if (rows.Count == 0) body.Children.Add(Label("No model requests recorded today.", "Muted", 13));
    }
    private void ShowToolFilters()
    {
        var body = ShowModal("Filter tools", 660);
        if (JobFilters.Parent is Panel parent) parent.Children.Remove(JobFilters);
        JobFilters.HorizontalAlignment = HorizontalAlignment.Stretch;
        JobFilters.Visibility = Visibility.Visible;
        body.Children.Add(JobFilters);
    }
}
