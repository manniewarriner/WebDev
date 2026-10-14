using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Animation;

namespace OrchDesk.App.Controls;

/// <summary>
/// The logo. When IsThinking is true the orbit dot circles the "O" and the three dots ripple in sequence.
/// If Windows animations are turned off (accessibility setting), the dots just show solid instead.
/// </summary>
public partial class ThinkingMark : UserControl
{
    public static readonly DependencyProperty IsThinkingProperty = DependencyProperty.Register(
        nameof(IsThinking), typeof(bool), typeof(ThinkingMark), new PropertyMetadata(false, (d, _) => ((ThinkingMark)d).Update()));

    private Storyboard? _storyboard;

    public ThinkingMark()
    {
        InitializeComponent();
        Loaded += (_, _) => Update();
        Unloaded += (_, _) => { _storyboard?.Stop(this); _storyboard = null; };
    }

    public bool IsThinking
    {
        get => (bool)GetValue(IsThinkingProperty);
        set => SetValue(IsThinkingProperty, value);
    }

    private void Update()
    {
        _storyboard?.Stop(this);
        _storyboard = null;
        Dot1.Opacity = Dot2.Opacity = Dot3.Opacity = 0;
        if (!IsThinking) return;

        if (!SystemParameters.ClientAreaAnimation)
        {
            Dot1.Opacity = Dot2.Opacity = Dot3.Opacity = 1;
            return;
        }

        var sb = new Storyboard { RepeatBehavior = RepeatBehavior.Forever };
        var orbit = new DoubleAnimation(0, 360, TimeSpan.FromSeconds(1.6));
        Storyboard.SetTarget(orbit, Orbit);
        Storyboard.SetTargetProperty(orbit, new PropertyPath("RenderTransform.Angle"));
        sb.Children.Add(orbit);

        var i = 0;
        foreach (var dot in new[] { Dot1, Dot2, Dot3 })
        {
            dot.RenderTransform = new System.Windows.Media.TranslateTransform();
            var bounce = new DoubleAnimation(0, -3, TimeSpan.FromSeconds(0.4)) { AutoReverse = true, BeginTime = TimeSpan.FromMilliseconds(180 * i), RepeatBehavior = RepeatBehavior.Forever, EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut } };
            Storyboard.SetTarget(bounce, dot);
            Storyboard.SetTargetProperty(bounce, new PropertyPath("RenderTransform.Y"));
            sb.Children.Add(bounce);
            var a = new DoubleAnimationUsingKeyFrames { BeginTime = TimeSpan.FromMilliseconds(180 * i++), Duration = TimeSpan.FromSeconds(1.2) };
            a.KeyFrames.Add(new EasingDoubleKeyFrame(0.15, KeyTime.FromTimeSpan(TimeSpan.Zero)));
            a.KeyFrames.Add(new EasingDoubleKeyFrame(1.0, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(300)), new SineEase()));
            a.KeyFrames.Add(new EasingDoubleKeyFrame(0.15, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(700)), new SineEase()));
            Storyboard.SetTarget(a, dot);
            Storyboard.SetTargetProperty(a, new PropertyPath(OpacityProperty));
            sb.Children.Add(a);
        }
        sb.Duration = TimeSpan.FromSeconds(1.6);
        _storyboard = sb;
        sb.Begin(this, true);
    }
}
