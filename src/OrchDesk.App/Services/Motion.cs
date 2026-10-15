using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace OrchDesk.App.Services;

public static class Motion
{
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
