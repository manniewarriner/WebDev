using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using OrchDesk.App.Services;

namespace OrchDesk.App.Controls;

/// <summary>Three inexpensive radial brushes, only ticking while the welcome screen is active.</summary>
public sealed class AuroraBackground : FrameworkElement
{
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(50) };
    private double _phase;
    public AuroraBackground()
    {
        IsHitTestVisible = false;
        _timer.Tick += (_, _) => { var window = Window.GetWindow(this); if (!Motion.Enabled || !IsVisible || window?.IsActive != true) return; _phase += .012; InvalidateVisual(); };
        Loaded += (_, _) => { _timer.Start(); Motion.Changed += Refresh; };
        Unloaded += (_, _) => { _timer.Stop(); Motion.Changed -= Refresh; };
    }
    private void Refresh() => InvalidateVisual();
    protected override void OnRender(DrawingContext dc)
    {
        var color = (Color)FindResource("AccentColor");
        for (var i = 0; i < 3; i++) {
            var brush = new RadialGradientBrush(Color.FromArgb((byte)(i == 0 ? 30 : 14), color.R, color.G, color.B), Colors.Transparent);
            dc.DrawEllipse(brush, null, new Point(ActualWidth * (.2 + i * .3) + Math.Sin(_phase + i) * 70, ActualHeight * .45 + Math.Cos(_phase + i) * 80), ActualWidth * .4, ActualHeight * .6);
        }
    }
}
