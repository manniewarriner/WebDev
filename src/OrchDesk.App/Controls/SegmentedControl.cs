using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using OrchDesk.App.Services;

namespace OrchDesk.App.Controls;

public sealed class SegmentedControl : Border
{
    public static readonly DependencyProperty SelectedIndexProperty = DependencyProperty.Register(nameof(SelectedIndex), typeof(int), typeof(SegmentedControl), new FrameworkPropertyMetadata(0, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, (d, _) => ((SegmentedControl)d).Slide()));
    public static readonly DependencyProperty FirstProperty = DependencyProperty.Register(nameof(First), typeof(string), typeof(SegmentedControl), new PropertyMetadata("Chat", (d, _) => ((SegmentedControl)d).UpdateLabels()));
    public static readonly DependencyProperty SecondProperty = DependencyProperty.Register(nameof(Second), typeof(string), typeof(SegmentedControl), new PropertyMetadata("Overnight", (d, _) => ((SegmentedControl)d).UpdateLabels()));
    public string First { get => (string)GetValue(FirstProperty); set => SetValue(FirstProperty, value); }
    public string Second { get => (string)GetValue(SecondProperty); set => SetValue(SecondProperty, value); }
    public int SelectedIndex { get => (int)GetValue(SelectedIndexProperty); set => SetValue(SelectedIndexProperty, value); }
    public event Action<int>? SelectionChanged;
    private readonly TranslateTransform _slide = new();
    private readonly Border _indicator = new();
    private readonly Button[] _buttons = [new(), new()];
    public SegmentedControl()
    {
        SetResourceReference(BackgroundProperty, "RaisedBrush"); SetResourceReference(BorderBrushProperty, "BorderBrush"); BorderThickness = new Thickness(1); CornerRadius = new CornerRadius(6); Padding = new Thickness(3); MinWidth = 176;
        var grid = new Grid(); grid.ColumnDefinitions.Add(new()); grid.ColumnDefinitions.Add(new()); Child = grid;
        _indicator.SetResourceReference(BackgroundProperty, "Surface3Brush"); _indicator.CornerRadius = new CornerRadius(4); _indicator.HorizontalAlignment = HorizontalAlignment.Left; _indicator.RenderTransform = _slide; Grid.SetColumnSpan(_indicator, 2); grid.Children.Add(_indicator);
        for (var i = 0; i < 2; i++) { var index = i; var button = _buttons[i]; button.Style = TryFindResource("GhostButton") as Style; button.Padding = new Thickness(8, 4, 8, 4); button.MinHeight = 28; Grid.SetColumn(button, i); grid.Children.Add(button); button.Click += (_, _) => { SetCurrentValue(SelectedIndexProperty, index); SelectionChanged?.Invoke(index); }; }
        UpdateLabels(); SizeChanged += (_, _) => Slide(); Loaded += (_, _) => { Motion.Changed += Slide; Slide(); }; Unloaded += (_, _) => { Motion.Changed -= Slide; _slide.BeginAnimation(TranslateTransform.XProperty, null); };
    }
    private void UpdateLabels() { if (_buttons is null) return; _buttons[0].Content = First; _buttons[1].Content = Second; }
    private void Slide()
    {
        var width = Math.Max(0, ActualWidth - 8) / 2; _indicator.Width = width; var from = _slide.X; _slide.BeginAnimation(TranslateTransform.XProperty, null); _slide.X = width * Math.Clamp(SelectedIndex, 0, 1);
        if (Motion.Enabled && IsVisible) _slide.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(from, _slide.X, TimeSpan.FromMilliseconds(180)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }, FillBehavior = FillBehavior.Stop });
        for (var i = 0; i < 2; i++) _buttons[i].SetResourceReference(Control.ForegroundProperty, i == SelectedIndex ? "TextBrush" : "MutedBrush");
    }
}

