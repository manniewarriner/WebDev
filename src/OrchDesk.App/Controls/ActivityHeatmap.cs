using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using OrchDesk.App.Services;
using OrchDesk.Core.Activity;
namespace OrchDesk.App.Controls;

public sealed class ActivityHeatmap : StackPanel
{
    private IReadOnlyList<ActivityDay> _days = [];
    public ActivityHeatmap() { Loaded += (_, _) => { Motion.Changed += MotionChanged; Populate(_days); }; Unloaded += (_, _) => Motion.Changed -= MotionChanged; }
    private void MotionChanged() { if (!Motion.Enabled) Populate(_days); }
    public void Populate(IReadOnlyList<ActivityDay> days)
    {
        _days = days;
        Children.Clear();
        var grid = new Grid { Margin = new Thickness(0, 12, 0, 8) };
        for (int x = 0; x < 26; x++) grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(13) });
        for (int y = 0; y < 7; y++) grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(13) });
        int index = 0;
        foreach (var day in days)
        {
            var cell = new Border { Width = 10, Height = 10, CornerRadius = new CornerRadius(2), Margin = new Thickness(0, 0, 3, 3), Opacity = 1, ToolTip = $"{day.Messages} messages · {day.Runs} runs · {day.Date:dd MMM yyyy}", RenderTransformOrigin = new Point(.5, .5), RenderTransform = new ScaleTransform(1, 1) };
            cell.SetResourceReference(Border.BackgroundProperty, day.Level == 0 ? "BorderBrush" : "AccentBrush");
            cell.Opacity = day.Level == 0 ? .22 : .2 + day.Level * .2;
            Grid.SetColumn(cell, index / 7); Grid.SetRow(cell, index % 7); grid.Children.Add(cell);
            if (Motion.Enabled) { var delay = TimeSpan.FromMilliseconds(index * 3); cell.BeginAnimation(OpacityProperty, new DoubleAnimation(0, cell.Opacity, TimeSpan.FromMilliseconds(260)) { BeginTime = delay, FillBehavior = FillBehavior.Stop }); foreach (var property in new[] { ScaleTransform.ScaleXProperty, ScaleTransform.ScaleYProperty }) ((ScaleTransform)cell.RenderTransform).BeginAnimation(property, new DoubleAnimation(.3, 1, TimeSpan.FromMilliseconds(260)) { BeginTime = delay, FillBehavior = FillBehavior.Stop }); }
            index++;
        }
        Children.Add(grid);
        var legend = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        legend.Children.Add(new TextBlock { Text = "Less", FontSize = 10, Margin = new Thickness(0, 0, 6, 0) });
        for (int i = 0; i < 5; i++) { var box = new Border { Width = 10, Height = 10, CornerRadius = new CornerRadius(2), Margin = new Thickness(2, 0, 2, 0), Opacity = i == 0 ? .22 : .2 + i * .2 }; box.SetResourceReference(Border.BackgroundProperty, i == 0 ? "BorderBrush" : "AccentBrush"); legend.Children.Add(box); }
        legend.Children.Add(new TextBlock { Text = "More", FontSize = 10, Margin = new Thickness(6, 0, 0, 0) }); Children.Add(legend);
    }
}
