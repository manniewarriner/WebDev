using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
namespace OrchDesk.App.Services;

public sealed class LiveCapture : IDisposable
{
    private readonly MainWindow window;
    private readonly string directory;
    private readonly DispatcherTimer timer = new() { Interval = TimeSpan.FromSeconds(30) };
    private int number;
    public LiveCapture(MainWindow window, string directory) {
        this.window = window; this.directory = Path.GetFullPath(directory); Directory.CreateDirectory(this.directory);
        timer.Tick += CaptureDuring; timer.Start();
    }
    private void CaptureDuring(object? sender, EventArgs e) {
        try { App.ApplyTheme("Dark"); window.UpdateLayout(); Save($"during-{++number:00}-dark.png"); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { timer.Stop(); Failure = ex; }
    }
    public Exception? Failure { get; private set; }
    private void Save(string name) {
        window.UpdateLayout();
        var root = (FrameworkElement)window.Content;
        var bitmap = new RenderTargetBitmap((int)Math.Ceiling(root.ActualWidth), (int)Math.Ceiling(root.ActualHeight), 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(root);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = File.Create(Path.Combine(directory, name)); encoder.Save(stream);
    }
    public async Task FinishAsync() {
        timer.Stop();
        if (Failure is not null) throw new IOException("Live capture failed.", Failure);
        foreach (var theme in new[] { "Dark", "Light" }) {
            App.ApplyTheme(theme);
            await window.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
            Save($"after-{theme.ToLowerInvariant()}.png");
        }
    }
    public void Dispose() => timer.Stop();
}
