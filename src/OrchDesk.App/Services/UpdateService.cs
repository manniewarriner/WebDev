using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Windows;
namespace OrchDesk.App.Services;

public static class UpdateService
{
    public static async Task CheckAsync(Window owner, bool screenshot)
    {
        if (screenshot) { MessageBox.Show(owner, "Update checks are disabled in screenshot mode.", "OrchDesk"); return; }
        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(20) }; http.DefaultRequestHeaders.UserAgent.ParseAdd("OrchDesk/0.3.0");
            using var json = JsonDocument.Parse(await http.GetStringAsync("https://api.github.com/repos/manniewarriner/WebDev/releases?per_page=100"));
            var latest = json.RootElement.EnumerateArray().Where(r => r.GetProperty("tag_name").GetString()!.StartsWith("orchdesk-v") && !r.GetProperty("prerelease").GetBoolean() && !r.GetProperty("draft").GetBoolean()).Select(r => new { Tag = r.GetProperty("tag_name").GetString()!, Published = r.GetProperty("published_at").GetDateTimeOffset() }).OrderByDescending(r => r.Published).FirstOrDefault();
            var current = typeof(UpdateService).Assembly.GetName().Version ?? new Version(0, 3, 0);
            if (latest is null || !Version.TryParse(latest.Tag[10..], out var available) || available <= new Version(current.Major, current.Minor, current.Build)) { MessageBox.Show(owner, "OrchDesk is up to date (" + current.ToString(3) + ").", "OrchDesk"); return; }
            if (MessageBox.Show(owner, $"OrchDesk {available} is available. Download and run the per-user installer? OrchDesk will close during the update.", "Update OrchDesk", MessageBoxButton.YesNo, MessageBoxImage.Information) != MessageBoxResult.Yes) return;
            var folder = Path.Combine(Path.GetTempPath(), "OrchDesk-update-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(folder); var script = Path.Combine(folder, "Install-OrchDesk.ps1");
            await File.WriteAllTextAsync(script, await http.GetStringAsync("https://raw.githubusercontent.com/manniewarriner/WebDev/orchdesk/self-improve/installer/Install-OrchDesk.ps1"));
            var start = new ProcessStartInfo("powershell.exe") { UseShellExecute = false, CreateNoWindow = true }; foreach (var arg in new[] { "-NoProfile", "-ExecutionPolicy", "Bypass", "-File", script, "-Update" }) start.ArgumentList.Add(arg); Process.Start(start);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or IOException or JsonException or System.ComponentModel.Win32Exception) { MessageBox.Show(owner, "Could not check for updates. Try again later or visit the GitHub releases page.", "OrchDesk"); }
    }
}
