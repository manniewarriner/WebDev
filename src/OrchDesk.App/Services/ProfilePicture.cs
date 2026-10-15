using System.IO;
using System.Windows;
using System.Windows.Media.Imaging;
namespace OrchDesk.App.Services;

public static class ProfilePicture
{
    public static void Pick(AppServices services, Window owner) { if (services.IsScreenshot) return; var dialog = new Microsoft.Win32.OpenFileDialog { Filter = "Images|*.png;*.jpg;*.jpeg;*.bmp" }; if (dialog.ShowDialog(owner) != true) return; try { var bitmap = new BitmapImage(); bitmap.BeginInit(); bitmap.CacheOption = BitmapCacheOption.OnLoad; bitmap.DecodePixelWidth = 512; bitmap.UriSource = new Uri(dialog.FileName); bitmap.EndInit(); var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap)); Directory.CreateDirectory(services.Store.Directory); using var output = File.Create(Path.Combine(services.Store.Directory, "avatar.png")); encoder.Save(output); } catch (Exception ex) when (ex is IOException or NotSupportedException or System.ArgumentException) { MessageBox.Show(owner, "This image could not be opened.", "OrchDesk"); } }
}
