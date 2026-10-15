using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;

namespace OrchDesk.App.Services;

public static class Motion
{
    public static readonly DependencyProperty AnimateSwitchProperty = DependencyProperty.RegisterAttached("AnimateSwitch", typeof(bool), typeof(Motion), new PropertyMetadata(false, (d, e) => {
        if (d is not CheckBox box) return;
        if ((bool)e.NewValue) { box.Checked += SwitchChanged; box.Unchecked += SwitchChanged; box.Indeterminate += SwitchChanged; }
        else { box.Checked -= SwitchChanged; box.Unchecked -= SwitchChanged; box.Indeterminate -= SwitchChanged; }
    }));
    public static bool GetAnimateSwitch(DependencyObject element) => (bool)element.GetValue(AnimateSwitchProperty);
    public static void SetAnimateSwitch(DependencyObject element, bool value) => element.SetValue(AnimateSwitchProperty, value);
    private static void SwitchChanged(object sender, RoutedEventArgs e)
    {
        if (!Enabled || sender is not CheckBox box || !box.IsVisible || box.Template.FindName("Thumb", box) is not FrameworkElement thumb) return;
        var transform = new TranslateTransform(); thumb.RenderTransform = transform;
        var from = box.IsChecked == true ? -16 : box.IsChecked == false ? 16 : 8;
        transform.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimationUsingKeyFrames {
            Duration = TimeSpan.FromMilliseconds(220), FillBehavior = FillBehavior.Stop,
            KeyFrames = { new LinearDoubleKeyFrame(from, KeyTime.FromTimeSpan(TimeSpan.Zero)), new EasingDoubleKeyFrame(-Math.Sign(from) * 1.5, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(160)), new CubicEase { EasingMode = EasingMode.EaseOut }), new EasingDoubleKeyFrame(0, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(220))) }
        });
    }
    public static void MorphOut(FrameworkElement element, FrameworkElement? origin, Action complete)
    {
        if (!Enabled || origin is null || !origin.IsVisible) { complete(); return; }
        var source = origin.TranslatePoint(new Point(origin.ActualWidth / 2, origin.ActualHeight / 2), element);
        var scale = new ScaleTransform(1, 1); var translate = new TranslateTransform(); var group = new TransformGroup(); group.Children.Add(scale); group.Children.Add(translate); element.RenderTransformOrigin = new Point(.5, .5); element.RenderTransform = group;
        var duration = TimeSpan.FromMilliseconds(160); var ease = new CubicEase { EasingMode = EasingMode.EaseIn };
        DoubleAnimation Animate(double to) => new(to, duration) { EasingFunction = ease, FillBehavior = FillBehavior.HoldEnd };
        scale.BeginAnimation(ScaleTransform.ScaleXProperty, Animate(Math.Clamp(origin.ActualWidth / Math.Max(1, element.ActualWidth), .15, .8))); scale.BeginAnimation(ScaleTransform.ScaleYProperty, Animate(Math.Clamp(origin.ActualHeight / Math.Max(1, element.ActualHeight), .08, .8)));
        translate.BeginAnimation(TranslateTransform.XProperty, Animate(source.X - element.ActualWidth / 2)); translate.BeginAnimation(TranslateTransform.YProperty, Animate(source.Y - element.ActualHeight / 2));
        var opacity = new DoubleAnimation(0, duration); opacity.Completed += (_, _) => complete(); element.BeginAnimation(UIElement.OpacityProperty, opacity);
    }
    public static readonly DependencyProperty RevealOnLoadProperty = DependencyProperty.RegisterAttached(
        "RevealOnLoad", typeof(bool), typeof(Motion), new PropertyMetadata(false, (d, e) => {
            if (d is not FrameworkElement element) return;
            if ((bool)e.NewValue) element.Loaded += OnLoaded;
            else element.Loaded -= OnLoaded;
        }));
    public static bool GetRevealOnLoad(DependencyObject element) => (bool)element.GetValue(RevealOnLoadProperty);
    public static void SetRevealOnLoad(DependencyObject element, bool value) => element.SetValue(RevealOnLoadProperty, value);
    private static void OnLoaded(object sender, RoutedEventArgs e) => Reveal((FrameworkElement)sender);
    public static void StopReveal(FrameworkElement element)
    {
        element.BeginAnimation(UIElement.OpacityProperty, null);
        if (element.RenderTransform is TranslateTransform translate) translate.BeginAnimation(TranslateTransform.YProperty, null);
    }
    private static void OnUnloaded(object sender, RoutedEventArgs e) => StopReveal((FrameworkElement)sender);
    private static void OnVisibilityChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (!(bool)e.NewValue) StopReveal((FrameworkElement)sender);
    }
    public static bool? ReduceMotion { get; set; }
    public static bool Screenshot { get; set; }
    public static event Action? Changed;
    public static bool Enabled => !Screenshot && !(ReduceMotion ?? !SystemParameters.ClientAreaAnimation);
    static Motion() => SystemParameters.StaticPropertyChanged += (_, _) => Changed?.Invoke();
    public static void Refresh() => Changed?.Invoke();
    public static void Morph(FrameworkElement element, FrameworkElement? origin)
    {
        if (!Enabled || origin is null) return;
        element.Dispatcher.BeginInvoke(() => {
            if (!Enabled || !element.IsVisible || !origin.IsVisible || PresentationSource.FromVisual(origin) != PresentationSource.FromVisual(element)) return;
            var source = origin.TranslatePoint(new Point(origin.ActualWidth / 2, origin.ActualHeight / 2), element);
            var scale = new ScaleTransform(Math.Clamp(origin.ActualWidth / Math.Max(1, element.ActualWidth), .15, .8), Math.Clamp(origin.ActualHeight / Math.Max(1, element.ActualHeight), .08, .8));
            var translate = new TranslateTransform(source.X - element.ActualWidth / 2, source.Y - element.ActualHeight / 2);
            var group = new TransformGroup(); group.Children.Add(scale); group.Children.Add(translate); element.RenderTransformOrigin = new Point(.5, .5); element.RenderTransform = group;
            var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
            DoubleAnimation Animate(double from, double to) => new(from, to, TimeSpan.FromMilliseconds(220)) { EasingFunction = ease, FillBehavior = FillBehavior.Stop };
            var x = scale.ScaleX; var y = scale.ScaleY; scale.ScaleX = scale.ScaleY = 1; scale.BeginAnimation(ScaleTransform.ScaleXProperty, Animate(x, 1)); scale.BeginAnimation(ScaleTransform.ScaleYProperty, Animate(y, 1));
            x = translate.X; y = translate.Y; translate.X = translate.Y = 0; translate.BeginAnimation(TranslateTransform.XProperty, Animate(x, 0)); translate.BeginAnimation(TranslateTransform.YProperty, Animate(y, 0));
            element.BeginAnimation(UIElement.OpacityProperty, Animate(0, 1));
        }, System.Windows.Threading.DispatcherPriority.Loaded);
    }
    public static void Reveal(FrameworkElement element)
    {
        element.Unloaded -= OnUnloaded;
        element.Unloaded += OnUnloaded;
        element.IsVisibleChanged -= OnVisibilityChanged;
        element.IsVisibleChanged += OnVisibilityChanged;
        element.BeginAnimation(UIElement.OpacityProperty, null);
        element.Opacity = 1;
        element.RenderTransform = new TranslateTransform();
        if (!Enabled || !element.IsVisible) return;
        var ease = new CubicEase { EasingMode = EasingMode.EaseOut }; ease.Freeze();
        element.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(180)) { FillBehavior = FillBehavior.Stop });
        ((TranslateTransform)element.RenderTransform).BeginAnimation(TranslateTransform.YProperty,
            new DoubleAnimation(6, 0, TimeSpan.FromMilliseconds(180)) { EasingFunction = ease, FillBehavior = FillBehavior.Stop });
    }
}
