using System.Globalization;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using OrchDesk.Core.Telemetry;
using OrchDesk.App.ViewModels;

namespace OrchDesk.App.Views;

// Token usage as a contribution grid (one cell per day, weeks as columns) that morphs into an isometric skyline: each day becomes a tower as tall as its
// token count. Hover or use the arrow keys to read a day, drag to orbit once it is a skyline.
public sealed class SkylineChart : FrameworkElement
{
    public static readonly string[] PaletteNames = ["Ember", "Moss", "Ocean", "Violet", "Rose", "Graphite"];
    private static readonly Color[][] Palettes =
    [
        Colors5("#3A2A22", "#7A3B1C", "#B84F1A", "#F2691F", "#FFB070"),
        Colors5("#22302A", "#2F6B45", "#3E9A5E", "#57D07F", "#B4F5C8"),
        Colors5("#222C3A", "#2A4F86", "#3573C4", "#4FA0FF", "#A8D2FF"),
        Colors5("#2C2438", "#59388F", "#7B4FC4", "#A678F5", "#D6BFFF"),
        Colors5("#38242C", "#8C2F55", "#C43E76", "#F55C9B", "#FFB3D0"),
        Colors5("#2A2A2A", "#555555", "#808080", "#B8B8B8", "#F0F0F0")
    ];
    private static Color[] Colors5(params string[] hex) => hex.Select(h => (Color)ColorConverter.ConvertFromString(h)).ToArray();

    public static readonly DependencyProperty MorphProperty = DependencyProperty.Register(nameof(Morph), typeof(double), typeof(SkylineChart), new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsRender));
    public static readonly DependencyProperty PaletteProperty = DependencyProperty.Register(nameof(Palette), typeof(int), typeof(SkylineChart), new FrameworkPropertyMetadata(0, FrameworkPropertyMetadataOptions.AffectsRender));
    public double Morph { get => (double)GetValue(MorphProperty); set => SetValue(MorphProperty, value); }
    public int Palette { get => (int)GetValue(PaletteProperty); set => SetValue(PaletteProperty, Math.Clamp(value, 0, Palettes.Length - 1)); }

    public event Action<DayUsage?>? SelectionChanged;
    private IReadOnlyList<DayUsage> days = [];
    private int weeks, leadBlanks;
    private int selected = -1;
    private double yaw = Math.PI / 8;
    private Point? dragFrom; private double dragYaw;
    private readonly List<(int Index, Geometry Top)> hits = [];
    public bool IsSkyline { get; private set; }

    public SkylineChart() { Focusable = true; FocusVisualStyle = null; ClipToBounds = true; Cursor = Cursors.Hand; }

    public void SetData(IReadOnlyList<DayUsage> data)
    {
        days = data; selected = -1;
        leadBlanks = data.Count == 0 ? 0 : (int)data[0].Date.DayOfWeek; // weeks start on Sunday
        weeks = (leadBlanks + data.Count + 6) / 7;
        InvalidateVisual(); SelectionChanged?.Invoke(null);
    }

    public void SetSkyline(bool skyline)
    {
        IsSkyline = skyline;
        if (!SystemParameters.ClientAreaAnimation) { BeginAnimation(MorphProperty, null); Morph = skyline ? 1 : 0; return; }
        BeginAnimation(MorphProperty, new DoubleAnimation(skyline ? 1 : 0, TimeSpan.FromMilliseconds(900)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseInOut } });
    }

    private (int Week, int Row) Cell(int index) { int slot = index + leadBlanks; return (slot / 7, slot % 7); }
    private static int Level(long tokens, long max) => tokens <= 0 || max <= 0 ? 0 : Math.Clamp((int)Math.Ceiling(tokens / (double)max * 4), 1, 4);

    protected override void OnRender(DrawingContext dc)
    {
        hits.Clear(); dc.DrawRectangle(Brushes.Transparent, null, new Rect(0, 0, ActualWidth, ActualHeight));
        if (days.Count == 0 || ActualWidth < 40 || ActualHeight < 40) return;
        var palette = Palettes[Palette]; long max = days.Max(d => d.Tokens); double m = Morph;
        double cx = weeks / 2.0, cy = 3.5, theta = yaw * m, cos = Math.Cos(theta), sin = Math.Sin(theta);
        double flatCell = Math.Min((ActualWidth - 16) / weeks, (ActualHeight - 16) / 7.0);
        double isoK = Math.Min((ActualWidth - 24) / ((weeks + 7) * 0.8660254), (ActualHeight - 24) / ((weeks + 7) * 0.5 + 6));
        double towerPx = Math.Max(10, isoK * 5.5 * Math.Min(1.0, 14.0 / Math.Max(1, weeks / 2.0)));
        double flatLeft = (ActualWidth - flatCell * weeks) / 2, flatTop = (ActualHeight - flatCell * 7) / 2;
        double isoMidX = ActualWidth / 2, isoBaseY = ActualHeight - 12 - isoK * ((weeks + 7) * 0.25) - 4;

        Point Proj(double gx, double gy, double z)
        {
            var flat = new Point(flatLeft + gx * flatCell, flatTop + gy * flatCell);
            double u = (gx - cx) * cos - (gy - cy) * sin, v = (gx - cx) * sin + (gy - cy) * cos;
            var iso = new Point(isoMidX + (u - v) * 0.8660254 * isoK, isoBaseY + (u + v) * 0.5 * isoK - z * towerPx * m);
            return new(flat.X + (iso.X - flat.X) * m, flat.Y + (iso.Y - flat.Y) * m);
        }
        double Depth(int w, int r) { double u = (w + 0.5 - cx) * cos - (r + 0.5 - cy) * sin, v = (w + 0.5 - cx) * sin + (r + 0.5 - cy) * cos; return u + v; }

        var order = Enumerable.Range(0, days.Count).OrderBy(i => { var (w, r) = Cell(i); return Depth(w, r); }).ToList();
        foreach (int i in order)
        {
            var (w, r) = Cell(i); var day = days[i]; var color = palette[Level(day.Tokens, max)];
            double h = day.Tokens <= 0 ? 0.04 : 0.12 + 0.88 * day.Tokens / max; // tower height as a fraction of the tallest
            double gap = 0.08; double x0 = w + gap, x1 = w + 1 - gap, y0 = r + gap, y1 = r + 1 - gap;
            var top = new[] { Proj(x0, y0, h), Proj(x1, y0, h), Proj(x1, y1, h), Proj(x0, y1, h) };
            var pen = i == selected ? new Pen(Brushes.White, 1.6) : null;
            if (m > 0.02)
            {
                // Side faces whose outward normal turns toward the viewer (larger u+v after rotation).
                (double nx, double ny, Point[] quad)[] sides =
                [
                    (1, 0, [Proj(x1, y0, h), Proj(x1, y1, h), Proj(x1, y1, 0), Proj(x1, y0, 0)]),
                    (-1, 0, [Proj(x0, y0, h), Proj(x0, y1, h), Proj(x0, y1, 0), Proj(x0, y0, 0)]),
                    (0, 1, [Proj(x0, y1, h), Proj(x1, y1, h), Proj(x1, y1, 0), Proj(x0, y1, 0)]),
                    (0, -1, [Proj(x0, y0, h), Proj(x1, y0, h), Proj(x1, y0, 0), Proj(x0, y0, 0)])
                ];
                foreach (var (nx, ny, quad) in sides)
                {
                    double nu = nx * cos - ny * sin, nv = nx * sin + ny * cos; if (nu + nv <= 0.0001) continue;
                    double shade = 0.45 + 0.35 * Math.Clamp((nu - nv + 1.4) / 2.8, 0, 1);
                    dc.DrawGeometry(new SolidColorBrush(Shade(color, shade)), null, Poly(quad));
                }
            }
            var topGeo = Poly(top); dc.DrawGeometry(new SolidColorBrush(color), pen, topGeo); hits.Add((i, topGeo));
        }
    }

    private static Color Shade(Color c, double f) => Color.FromRgb((byte)(c.R * f), (byte)(c.G * f), (byte)(c.B * f));
    private static Geometry Poly(Point[] pts)
    {
        var g = new StreamGeometry(); using (var c = g.Open()) { c.BeginFigure(pts[0], true, true); c.PolyLineTo(pts.Skip(1).ToArray(), true, false); }
        g.Freeze(); return g;
    }

    private int HitIndex(Point p) { for (int k = hits.Count - 1; k >= 0; k--) if (hits[k].Top.FillContains(p)) return hits[k].Index; return -1; }
    private void Select(int index) { if (index == selected) return; selected = index; InvalidateVisual(); SelectionChanged?.Invoke(index >= 0 && index < days.Count ? days[index] : null); }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        var p = e.GetPosition(this);
        if (dragFrom is { } from && IsSkyline) { yaw = dragYaw + (p.X - from.X) * 0.01; InvalidateVisual(); return; }
        Select(HitIndex(p));
    }
    protected override void OnMouseLeave(MouseEventArgs e) { if (dragFrom == null) Select(-1); }
    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e) { Focus(); if (IsSkyline) { dragFrom = e.GetPosition(this); dragYaw = yaw; CaptureMouse(); } }
    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e) { dragFrom = null; ReleaseMouseCapture(); }
    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (days.Count == 0) return;
        int step = e.Key switch { Key.Left => -1, Key.Right => 1, Key.Up => -7, Key.Down => 7, _ => 0 };
        if (step == 0) return;
        Select(selected < 0 ? days.Count - 1 : Math.Clamp(selected + step, 0, days.Count - 1)); e.Handled = true;
    }
    protected override void OnRenderSizeChanged(SizeChangedInfo info) { base.OnRenderSizeChanged(info); InvalidateVisual(); }

    public static string Describe(DayUsage d) => $"{d.Date.ToString("ddd d MMM yyyy", CultureInfo.CurrentCulture)} · {(d.Tokens == 0 ? "no usage" : d.Tokens.ToString("N0", CultureInfo.CurrentCulture) + " tokens · " + d.Calls + " model call" + (d.Calls == 1 ? "" : "s"))}";
}
