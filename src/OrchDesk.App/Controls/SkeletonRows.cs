using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using OrchDesk.App.Services;

namespace OrchDesk.App.Controls;

public sealed class SkeletonRows : StackPanel
{
    public SkeletonRows()
    {
        IsHitTestVisible = false;
        for (var i = 0; i < 3; i++) { var row = new Border { Height = 12, Margin = new Thickness(0, 4, i * 12, 4), CornerRadius = new CornerRadius(6) }; row.SetResourceReference(BackgroundProperty, "Surface3Brush"); Children.Add(row); }
        Loaded += (_, _) => { Motion.Changed += Animate; Animate(); }; Unloaded += (_, _) => { Motion.Changed -= Animate; BeginAnimation(OpacityProperty, null); }; IsVisibleChanged += (_, _) => Animate();
    }
    private void Animate()
    {
        BeginAnimation(OpacityProperty, null); Opacity = .65;
        if (Motion.Enabled && IsVisible) BeginAnimation(OpacityProperty, new DoubleAnimation(.35, .85, TimeSpan.FromSeconds(1.2)) { AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever });
    }
}
