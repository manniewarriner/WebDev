using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Markup;
using System.Windows.Media.Animation;
using OrchDesk.App.Services;
using OrchDesk.Core.Filtering;
namespace OrchDesk.App.Controls;

public sealed class FilterBar : WrapPanel
{
    public Dictionary<string, string[]> Fields { get; } = new();
    public List<FilterModel> Filters { get; } = [];
    public event Action? Changed;
    private static readonly ControlTemplate ComboTemplate = (ControlTemplate)XamlReader.Parse("""
        <ControlTemplate xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation" xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml" TargetType="ComboBox">
          <Grid>
            <Border Background="{TemplateBinding Background}" CornerRadius="3" Padding="4,3,18,3"><Grid><ContentPresenter x:Name="Selection" Content="{TemplateBinding SelectionBoxItem}" VerticalAlignment="Center"/><TextBox x:Name="PART_EditableTextBox" Style="{x:Null}" Visibility="Hidden" Background="Transparent" BorderThickness="0" Padding="0" Foreground="{TemplateBinding Foreground}" FontSize="{TemplateBinding FontSize}"/></Grid></Border>
            <ToggleButton HorizontalAlignment="Right" Width="16" Padding="0" Background="Transparent" BorderThickness="0" Focusable="False" Content="&#x2304;" IsChecked="{Binding IsDropDownOpen,RelativeSource={RelativeSource TemplatedParent},Mode=TwoWay}"/>
            <Popup x:Name="PART_Popup" IsOpen="{TemplateBinding IsDropDownOpen}" Placement="Bottom" AllowsTransparency="True"><Border Background="{DynamicResource RaisedBrush}" BorderBrush="{DynamicResource BorderBrush}" BorderThickness="1" Padding="4" MinWidth="90"><ScrollViewer MaxHeight="200"><ItemsPresenter/></ScrollViewer></Border></Popup>
          </Grid>
          <ControlTemplate.Triggers><Trigger Property="IsEditable" Value="True"><Setter TargetName="Selection" Property="Visibility" Value="Hidden"/><Setter TargetName="PART_EditableTextBox" Property="Visibility" Value="Visible"/></Trigger></ControlTemplate.Triggers>
        </ControlTemplate>
        """);
    public FilterBar() { Loaded += (_, _) => { Motion.Changed += MotionChanged; Rebuild(); }; Unloaded += (_, _) => Motion.Changed -= MotionChanged; }
    private void MotionChanged() { if (!Motion.Enabled) Rebuild(); }
    public void Add(string field, string value, FilterOperator op = FilterOperator.Is) { Filters.Add(new(field, op, value)); Rebuild(); Changed?.Invoke(); }
    private ComboBox Combo(IEnumerable<string> values, string value, double width, bool editable = false)
    {
        var combo = new ComboBox { ItemsSource = values, SelectedItem = value, Text = value, IsEditable = editable, FontSize = 10, MinWidth = width, MinHeight = 24, MaxWidth = 110, Padding = new Thickness(0), Template = ComboTemplate, Margin = new Thickness(2, 0, 2, 0) };
        combo.SetResourceReference(Control.ForegroundProperty, "TextBrush"); combo.SetResourceReference(BackgroundProperty, "SurfaceBrush"); return combo;
    }
    private Button SmallButton(string label) => new() { Content = label, MinHeight=24, Height=24, Padding = new Thickness(6, 4, 6, 4), Margin = new Thickness(0, 3, 4, 3), FontSize = 10 };
    private void Rebuild()
    {
        Children.Clear(); var add = SmallButton("+ Filter");
        add.Click += (_, _) => { var menu = new ContextMenu(); foreach (var field in Fields.Keys) { var item = new MenuItem { Header = field }; item.Click += (_, _) => Add(field, Fields[field].FirstOrDefault() ?? "", field == "Title" ? FilterOperator.Contains : FilterOperator.Is); menu.Items.Add(item); } add.ContextMenu = menu; menu.IsOpen = true; }; Children.Add(add);
        foreach (var original in Filters.ToArray())
        {
            var current = original;
            var row = new WrapPanel { VerticalAlignment = VerticalAlignment.Center };
            var chip = new Border { Child = row, CornerRadius = new CornerRadius(6), BorderThickness = new Thickness(1), Margin = new Thickness(0, 3, 4, 3), Padding = new Thickness(4), RenderTransformOrigin = new Point(.5, .5), RenderTransform = new ScaleTransform(1, 1) };
            chip.SetResourceReference(Border.BorderBrushProperty, "BorderBrush"); chip.SetResourceReference(Border.BackgroundProperty, "RaisedBrush");
            row.Children.Add(new TextBlock { Text = current.Field, VerticalAlignment = VerticalAlignment.Center, FontSize = 10, Margin = new Thickness(0, 0, 3, 0) });
            var op = Combo(["is", "is not", "contains"], current.Operator switch { FilterOperator.Is => "is", FilterOperator.IsNot => "is not", _ => "contains" }, 44); row.Children.Add(op);
            var value = Combo(Fields.GetValueOrDefault(current.Field) ?? [], current.Value, 58, current.Field == "Title" || current.Field == "Date"); row.Children.Add(value);
            void Update() { var i = Filters.IndexOf(current); if (i < 0) return; current = new(current.Field, (FilterOperator)Math.Max(0, op.SelectedIndex), value.IsEditable ? value.Text : value.SelectedItem?.ToString() ?? ""); Filters[i] = current; Changed?.Invoke(); }
            op.SelectionChanged += (_, _) => Update(); value.SelectionChanged += (_, _) => Dispatcher.BeginInvoke(Update); value.AddHandler(TextBox.TextChangedEvent, new TextChangedEventHandler((_, _) => Update()));
            var remove = SmallButton("\u00d7"); remove.Margin = new Thickness(2, 0, 0, 0); remove.Padding = new Thickness(3, 0, 3, 0);
            remove.Click += async (_, _) => { remove.IsEnabled = false; if (Motion.Enabled) { chip.BeginAnimation(OpacityProperty, new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(120))); foreach (var property in new[] { ScaleTransform.ScaleXProperty, ScaleTransform.ScaleYProperty }) ((ScaleTransform)chip.RenderTransform).BeginAnimation(property, new DoubleAnimation(1, .8, TimeSpan.FromMilliseconds(120))); await Task.Delay(120); } Filters.Remove(current); Rebuild(); Changed?.Invoke(); };
            row.Children.Add(remove); Children.Add(chip);
            if (Motion.Enabled) { chip.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(160))); foreach (var property in new[] { ScaleTransform.ScaleXProperty, ScaleTransform.ScaleYProperty }) ((ScaleTransform)chip.RenderTransform).BeginAnimation(property, new DoubleAnimation(.85, 1, TimeSpan.FromMilliseconds(160))); }
        }
        if (Filters.Count > 0) { var clear = SmallButton("Clear"); clear.Style = TryFindResource("GhostButton") as Style; clear.Click += (_, _) => { Filters.Clear(); Rebuild(); Changed?.Invoke(); }; Children.Add(clear); }
    }
}
