using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace OrchDesk.App.Controls;

/// <summary>WPF has no TextBlock character-spacing property. Draw labels with a native 0.06em advance.</summary>
public sealed class TrackingLabel : Control
{
    public static readonly DependencyProperty TextProperty = DependencyProperty.Register(nameof(Text), typeof(string), typeof(TrackingLabel), new FrameworkPropertyMetadata("", FrameworkPropertyMetadataOptions.AffectsMeasure | FrameworkPropertyMetadataOptions.AffectsRender));
    public string Text { get => (string)GetValue(TextProperty); set => SetValue(TextProperty, value); }
    public TrackingLabel() { SetResourceReference(FontFamilyProperty, "UiFont"); SetResourceReference(FontSizeProperty, "Type.Label"); SetResourceReference(ForegroundProperty, "MutedBrush"); }
    private FormattedText Glyph(char character) => new(character.ToString(), CultureInfo.CurrentUICulture, FlowDirection, new Typeface(FontFamily, FontStyle, FontWeight, FontStretch), FontSize, Foreground, VisualTreeHelper.GetDpi(this).PixelsPerDip);
    protected override Size MeasureOverride(Size constraint)
    {
        var text = (Text ?? "").ToUpperInvariant();
        return new Size(text.Sum(c => Glyph(c).WidthIncludingTrailingWhitespace + FontSize * .06), Math.Ceiling(FontSize * 1.5));
    }
    protected override void OnRender(DrawingContext dc)
    {
        double x = 0;
        foreach (var character in (Text ?? "").ToUpperInvariant()) { var glyph = Glyph(character); dc.DrawText(glyph, new Point(x, 0)); x += glyph.WidthIncludingTrailingWhitespace + FontSize * .06; }
    }
}
