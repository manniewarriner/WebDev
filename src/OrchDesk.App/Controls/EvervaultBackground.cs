using System.Globalization;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using OrchDesk.App.Services;
namespace OrchDesk.App.Controls;

public sealed class EvervaultBackground : FrameworkElement
{
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(150) };
    private ImageSource? _field;
    private Point _spot = new(.5, .5);
    private DateTime _last;
    public bool Dimmed { get; set; }
    public EvervaultBackground()
    {
        IsHitTestVisible = false; Opacity = .10;
        _timer.Tick += (_, _) => { if (Motion.Enabled && Window.GetWindow(this) is { IsActive: true, WindowState: not WindowState.Minimized }) Scramble(); };
        Loaded += (_, _) => { Scramble(); _timer.Start(); CompositionTarget.Rendering += Frame; Motion.Changed += MotionChanged; };
        Unloaded += (_, _) => { _timer.Stop(); CompositionTarget.Rendering -= Frame; Motion.Changed -= MotionChanged; }; SizeChanged += (_, _) => Scramble();
    }
    private void MotionChanged() { Scramble(); InvalidateVisual(); }
    private void Frame(object? sender, EventArgs e) { if (!Motion.Enabled || Window.GetWindow(this) is not { IsActive: true, WindowState: not WindowState.Minimized } || (DateTime.UtcNow - _last).TotalMilliseconds < 34) return; _last = DateTime.UtcNow; var p = System.Windows.Input.Mouse.GetPosition(this); if (ActualWidth > 0 && ActualHeight > 0) _spot = new Point(p.X / ActualWidth, p.Y / ActualHeight); InvalidateVisual(); }
    private void Scramble() { if (ActualWidth < 1 || ActualHeight < 1) return; var visual = new DrawingVisual(); using (var dc = visual.RenderOpen()) { var random = new Random(Motion.Screenshot ? 42 : Environment.TickCount); var brush = TryFindResource("AccentBrush") as Brush ?? Brushes.MediumPurple; for (int y = 0; y < ActualHeight; y += 22) { var row = new string(Enumerable.Range(0, (int)ActualWidth / 12 + 1).Select(_ => "01abcdef{}<>/+*"[random.Next(14)]).ToArray()); dc.DrawText(new FormattedText(row, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, new Typeface("Consolas"), 14, brush, 1), new Point(0, y)); } } var bitmap = new RenderTargetBitmap((int)ActualWidth, (int)ActualHeight, 96, 96, PixelFormats.Pbgra32); bitmap.Render(visual); bitmap.Freeze(); _field = bitmap; InvalidateVisual(); }
    protected override void OnRender(DrawingContext dc) { if (_field is null) return; Opacity = Dimmed ? .06 : .10; var mask = new RadialGradientBrush(Colors.White, Colors.Transparent) { Center = _spot, GradientOrigin = _spot, RadiusX = .48, RadiusY = .65 }; dc.PushOpacityMask(mask); dc.DrawImage(_field, new Rect(RenderSize)); dc.Pop(); }
}
