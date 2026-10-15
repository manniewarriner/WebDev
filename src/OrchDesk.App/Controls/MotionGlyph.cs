using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using OrchDesk.App.Services;

namespace OrchDesk.App.Controls;

/// <summary>Small status animation; clocks are removed whenever hidden or detached.</summary>
public sealed class MotionGlyph : TextBlock
{
    public static readonly DependencyProperty SpinProperty = DependencyProperty.Register(nameof(Spin), typeof(bool), typeof(MotionGlyph), new PropertyMetadata(false));
    public bool Spin { get => (bool)GetValue(SpinProperty); set => SetValue(SpinProperty, value); }
    public MotionGlyph()
    {
        RenderTransformOrigin = new Point(.5, .5);
        Loaded += (_, _) => { Motion.Changed += Update; Update(); };
        Unloaded += (_, _) => { Motion.Changed -= Update; Stop(); };
        IsVisibleChanged += (_, _) => Update();
    }
    private void Stop() { BeginAnimation(OpacityProperty, null); if (RenderTransform is RotateTransform r) r.BeginAnimation(RotateTransform.AngleProperty, null); }
    private void Update()
    {
        Stop(); Opacity = 1;
        if (!IsLoaded || !IsVisible || !Motion.Enabled) return;
        if (Spin) { var r = new RotateTransform(); RenderTransform = r; r.BeginAnimation(RotateTransform.AngleProperty, new DoubleAnimation(0, 360, TimeSpan.FromSeconds(1.2)) { RepeatBehavior = RepeatBehavior.Forever }); }
        else BeginAnimation(OpacityProperty, new DoubleAnimation(1, .1, TimeSpan.FromSeconds(.5)) { AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever });
    }
}
