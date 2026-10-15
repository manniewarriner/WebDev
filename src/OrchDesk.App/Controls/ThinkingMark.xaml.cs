using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using OrchDesk.App.Services;
namespace OrchDesk.App.Controls;
public partial class ThinkingMark : UserControl
{
    public static readonly DependencyProperty IsThinkingProperty = DependencyProperty.Register(nameof(IsThinking), typeof(bool), typeof(ThinkingMark), new PropertyMetadata(false, (d, _) => ((ThinkingMark)d).Update()));
    public bool IsThinking { get => (bool)GetValue(IsThinkingProperty); set => SetValue(IsThinkingProperty, value); }
    public ThinkingMark()
    {
        InitializeComponent();
        // Fibonacci sphere: fixed geometry, animated as one compositor transform.
        for (int i = 0; i < 150; i++) {
            double y = 1 - 2 * (i + .5) / 150, radius = Math.Sqrt(1 - y * y), angle = i * 2.399963;
            double x = Math.Cos(angle) * radius, z = Math.Sin(angle) * radius;
            var dot = new Ellipse { Width = 1.3 + (z + 1) * .5, Height = 1.3 + (z + 1) * .5, Opacity = .22 + (z + 1) * .35 };
            dot.SetResourceReference(Shape.FillProperty, "TextBrush");
            Canvas.SetLeft(dot, 23 + x * 19); Canvas.SetTop(dot, 23 + y * 19); Orb.Children.Add(dot);
        }
        Loaded += (_, _) => { Motion.Changed += Update; Update(); };
        Unloaded += (_, _) => { Motion.Changed -= Update; Stop(); };
        IsVisibleChanged += (_, _) => Update();
    }
    private void Stop()
    {
        ((RotateTransform)Orb.RenderTransform).BeginAnimation(RotateTransform.AngleProperty, null);
        foreach (var dot in new[] { Dot1, Dot2, Dot3 }) dot.BeginAnimation(OpacityProperty, null);
    }
    private void Update()
    {
        Stop(); Dot1.Opacity = 1; Dot2.Opacity = .65; Dot3.Opacity = .3;
        if (!IsLoaded || !IsVisible || !IsThinking || !Motion.Enabled) return;
        ((RotateTransform)Orb.RenderTransform).BeginAnimation(RotateTransform.AngleProperty, new DoubleAnimation(0, 360, TimeSpan.FromSeconds(12)) { RepeatBehavior = RepeatBehavior.Forever });
        int i = 0;
        foreach (var dot in new[] { Dot1, Dot2, Dot3 }) dot.BeginAnimation(OpacityProperty, new DoubleAnimation(.2, 1, TimeSpan.FromSeconds(.45)) { AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever, BeginTime = TimeSpan.FromMilliseconds(i++ * 170) });
    }
}
