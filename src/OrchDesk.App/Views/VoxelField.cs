using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace OrchDesk.App.Views;

// Animated voxel mosaic used as the backdrop: one bitmap pixel per cell, stretched with hard edges and crossed by a thin grid so every cell reads as a block.
// It dims toward the middle so chat text stays readable, pauses when the window is inactive, and holds a still frame when the system turns animations off.
// A fully transparent VoxelBrush (the light theme) switches it off.
public sealed class VoxelField : FrameworkElement
{
    public static readonly DependencyProperty CellProperty = DependencyProperty.Register(nameof(Cell), typeof(int), typeof(VoxelField), new FrameworkPropertyMetadata(10, FrameworkPropertyMetadataOptions.AffectsRender, (d, _) => ((VoxelField)d).Reset()));
    public int Cell { get => (int)GetValue(CellProperty); set => SetValue(CellProperty, Math.Clamp(value, 4, 40)); }

    private readonly DispatcherTimer timer = new(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(80) };
    private WriteableBitmap? bitmap;
    private byte[] pixels = [];
    private int cols, rows;
    private double time;
    private Brush? grid;
    private Color lastGround;

    public VoxelField()
    {
        IsHitTestVisible = false; SnapsToDevicePixels = true;
        RenderOptions.SetBitmapScalingMode(this, BitmapScalingMode.NearestNeighbor);
        timer.Tick += (_, _) => Tick();
        Loaded += (_, _) => { Reset(); if (SystemParameters.ClientAreaAnimation) timer.Start(); };
        Unloaded += (_, _) => timer.Stop();
    }

    private void Reset() { bitmap = null; grid = null; InvalidateVisual(); }
    protected override void OnRenderSizeChanged(SizeChangedInfo info) { base.OnRenderSizeChanged(info); Reset(); }

    private void Tick()
    {
        var window = Window.GetWindow(this);
        if (!IsVisible || window == null || !window.IsActive || window.WindowState == WindowState.Minimized) return;
        if (Tint().A == 0) return;
        time += 0.08; InvalidateVisual();
    }

    private Color Tint() => TryFindResource("VoxelBrush") is SolidColorBrush b ? b.Color : Color.FromRgb(0x3C, 0xA6, 0xFF);
    private Color Ground() => TryFindResource("BgBrush") is SolidColorBrush b ? b.Color : Colors.Black;

    protected override void OnRender(DrawingContext dc)
    {
        int cell = Cell; if (ActualWidth < cell || ActualHeight < cell) return;
        var tint = Tint(); if (tint.A == 0) return;
        var ground = Ground(); if (ground != lastGround) { lastGround = ground; bitmap = null; }
        if (bitmap == null)
        {
            cols = (int)Math.Ceiling(ActualWidth / cell); rows = (int)Math.Ceiling(ActualHeight / cell);
            bitmap = new(cols, rows, 96, 96, PixelFormats.Pbgra32, null); pixels = new byte[cols * rows * 4];
            var line = ground; line.A = 150;
            var tile = new DrawingGroup();
            tile.Children.Add(new GeometryDrawing(new SolidColorBrush(line), null, new RectangleGeometry(new Rect(cell - 1, 0, 1, cell))));
            tile.Children.Add(new GeometryDrawing(new SolidColorBrush(line), null, new RectangleGeometry(new Rect(0, cell - 1, cell, 1))));
            var brush = new DrawingBrush(tile) { TileMode = TileMode.Tile, Viewport = new Rect(0, 0, cell, cell), ViewportUnits = BrushMappingMode.Absolute, Viewbox = new Rect(0, 0, cell, cell), ViewboxUnits = BrushMappingMode.Absolute };
            brush.Freeze(); grid = brush;
        }
        Fill(tint);
        bitmap.WritePixels(new Int32Rect(0, 0, cols, rows), pixels, cols * 4, 0);
        var area = new Rect(0, 0, cols * cell, rows * cell);
        dc.PushClip(new RectangleGeometry(new Rect(0, 0, ActualWidth, ActualHeight)));
        dc.DrawImage(bitmap, area);
        if (grid != null) dc.DrawRectangle(grid, null, area);
        dc.Pop();
    }

    private void Fill(Color tint)
    {
        double aspect = (double)cols / rows;
        for (int y = 0; y < rows; y++)
        {
            double v = (double)y / rows;
            for (int x = 0; x < cols; x++)
            {
                double u = (double)x / cols, ax = u * aspect;
                // Two drifting waves plus a little per-cell flicker, quantised into steps so the cells look like blocks of different height.
                double n = 0.5 + 0.34 * Math.Sin(ax * 5.0 + time * 0.7) * Math.Sin(v * 4.0 - time * 0.5) + 0.16 * Math.Sin((ax + v) * 7.0 - time * 1.1);
                n += (Hash(x, y, (int)(time * 5)) - 0.5) * 0.16;
                double level = Math.Floor(Math.Clamp(n, 0, 1) * 8) / 8;
                double dx = Math.Abs(u - 0.5) * 2, centre = dx < 0.35 ? 0.16 : 0.16 + 0.84 * Smooth((dx - 0.35) / 0.4);
                double a = Math.Clamp((level - 0.4) / 0.6, 0, 1) * centre;
                int i = (y * cols + x) * 4;
                pixels[i] = (byte)(tint.B * a); pixels[i + 1] = (byte)(tint.G * a); pixels[i + 2] = (byte)(tint.R * a); pixels[i + 3] = (byte)(255 * a);
            }
        }
    }
    private static double Smooth(double t) { t = Math.Clamp(t, 0, 1); return t * t * (3 - 2 * t); }
    private static double Hash(int x, int y, int t) { unchecked { uint h = (uint)(x * 374761393 + y * 668265263 + t * 2147483647); h = (h ^ (h >> 13)) * 1274126177; return ((h ^ (h >> 16)) & 0xFFFF) / 65535.0; } }
}
