using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using OrchDesk.App.Services;
namespace OrchDesk.App.Controls;

public sealed class ShinyButton : Button
{
    private readonly TranslateTransform _sweep = new();
    public ShinyButton() { Style = TryFindResource("AccentButton") as Style; Loaded += (_, _) => Animate(); Unloaded += (_, _) => { Motion.Changed -= Animate; _sweep.BeginAnimation(TranslateTransform.XProperty, null); }; }
    public override void OnApplyTemplate() { base.OnApplyTemplate(); Animate(); }
    private void Animate()
    {
        Motion.Changed -= Animate; Motion.Changed += Animate;
        var brush = new LinearGradientBrush { StartPoint = new Point(0, 1), EndPoint = new Point(1, 0), RelativeTransform = _sweep };
        brush.GradientStops.Add(new GradientStop(Colors.Transparent, .25)); brush.GradientStops.Add(new GradientStop(Color.FromArgb(105, 255, 255, 255), .5)); brush.GradientStops.Add(new GradientStop(Colors.Transparent, .75));
        var baseColor = (TryFindResource("AccentBrush") as SolidColorBrush)?.Color ?? Colors.SlateBlue;
        var drawing = new DrawingGroup(); drawing.Children.Add(new GeometryDrawing(new SolidColorBrush(baseColor), null, new RectangleGeometry(new Rect(0, 0, 1, 1)))); drawing.Children.Add(new GeometryDrawing(brush, null, new RectangleGeometry(new Rect(0, 0, 1, 1))));
        Background = new DrawingBrush(drawing); SetResourceReference(ForegroundProperty, "AccentTextBrush"); Padding = new Thickness(32, 13, 32, 13);
        _sweep.BeginAnimation(TranslateTransform.XProperty, null); _sweep.X = Motion.Screenshot ? 0 : -1.5;
        if (Motion.Enabled) _sweep.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimationUsingKeyFrames { Duration = TimeSpan.FromSeconds(5), RepeatBehavior = RepeatBehavior.Forever, KeyFrames = { new LinearDoubleKeyFrame(-1.5, KeyTime.FromTimeSpan(TimeSpan.Zero)), new LinearDoubleKeyFrame(1.5, KeyTime.FromTimeSpan(TimeSpan.FromSeconds(1.3))), new DiscreteDoubleKeyFrame(-1.5, KeyTime.FromTimeSpan(TimeSpan.FromSeconds(5))) } });
    }
}
