using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using OrchDesk.Core.Telemetry;
using OrchDesk.App.ViewModels;

namespace OrchDesk.App.Views;

public partial class UsageView : UserControl
{
    private const int Days = 182; // 26 weeks
    public UsageView()
    {
        InitializeComponent();
        PaletteBox.ItemsSource = SkylineChart.PaletteNames; PaletteBox.SelectedIndex = 0;
        Chart.SelectionChanged += d => DetailText.Text = d == null ? "Hover a day, or click the chart and use the arrow keys." : SkylineChart.Describe(d);
    }

    // Used by the smoke test to capture the skyline without waiting for the morph animation.
    public void ForceSkyline() { Chart.SetSkyline(true); Chart.BeginAnimation(SkylineChart.MorphProperty, null); Chart.Morph = 1; MorphButton.Content = "Show grid"; }
    private void OnVisibleChanged(object sender, DependencyPropertyChangedEventArgs e) { if (e.NewValue is true) Reload(); }

    public void Reload()
    {
        if (DataContext is not MainViewModel vm) return;
        var summary = vm.Services.UsageLog.Summarize(Days);
        Chart.SetData(vm.Services.UsageLog.Daily(Days));
        TotalText.Text = Short(summary.Tokens); CallsText.Text = summary.Calls.ToString("N0", CultureInfo.CurrentCulture);
        BusiestText.Text = summary.BusiestDay is { } b ? $"{b.Date.ToString("d MMM", CultureInfo.CurrentCulture)} · {Short(b.Tokens)}" : "none yet";
        StreakText.Text = summary.Streak == 1 ? "1 day" : summary.Streak + " days";
        Models.ItemsSource = summary.ByModel.Select(x => new { x.Model, Tokens = x.Tokens.ToString("N0", CultureInfo.CurrentCulture) }).Take(8).ToList();
        EmptyText.Visibility = summary.Calls == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private static string Short(long n) => n >= 1_000_000 ? (n / 1_000_000.0).ToString("0.##", CultureInfo.CurrentCulture) + "M" : n >= 10_000 ? (n / 1000.0).ToString("0.#", CultureInfo.CurrentCulture) + "k" : n.ToString("N0", CultureInfo.CurrentCulture);
    private void OnMorph(object sender, RoutedEventArgs e) { Chart.SetSkyline(!Chart.IsSkyline); MorphButton.Content = Chart.IsSkyline ? "Show grid" : "Show skyline"; }
    private void OnPalette(object sender, SelectionChangedEventArgs e) { if (Chart != null) Chart.Palette = PaletteBox.SelectedIndex; }
}
