using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

using OrchDesk.Core.Automation;

namespace OrchDesk.Core.Tools;
public sealed class MeshyClient(Func<string?> apiKey, HttpClient? client = null)
{
    private const string BaseUrl = "https://api.meshy.ai";
    private const long MaxDownloadBytes = 200L * 1024 * 1024;
    private const long MaxImageBytes = 20L * 1024 * 1024;
    private const int MaxJsonChars = 2_000_000;
    private readonly HttpClient http = client ?? new() { Timeout = TimeSpan.FromMinutes(10) };
    public TimeSpan PollInterval { get; set; } = TimeSpan.FromSeconds(3);
    public TimeSpan OverallTimeout { get; set; } = TimeSpan.FromMinutes(15);
    public string TextToImageModel { get; set; } = "nano-banana";

    public async Task<string> TextToImageAsync(string prompt, string outputDir, Action<string>? progress, CancellationToken ct)
    {
        var key = Key(); if (string.IsNullOrWhiteSpace(prompt)) throw new ArgumentException("Prompt is required.", nameof(prompt));
        var id = await CreateAsync("/openapi/v1/text-to-image", new { ai_model = TextToImageModel, prompt, aspect_ratio = "1:1" }, key, ct);
        using var doc = await WaitAsync("/openapi/v1/text-to-image", id, key, progress, ct);
        if (!doc.RootElement.TryGetProperty("image_urls", out var urls) || urls.ValueKind != JsonValueKind.Array || urls.GetArrayLength() == 0) throw new InvalidOperationException("Meshy finished without an image.");
        var url = urls[0].GetString() ?? throw new InvalidOperationException("Meshy finished without an image.");
        return await DownloadAsync(url, outputDir, FileName(prompt, ".png"), ct);
    }

    public async Task<string> ImageTo3DAsync(string imagePath, string outputDir, Action<string>? progress, CancellationToken ct)
    {
        var key = Key();
        var mime = Path.GetExtension(imagePath).ToLowerInvariant() switch { ".png" => "image/png", ".jpg" or ".jpeg" => "image/jpeg", ".webp" => "image/webp", _ => throw new ArgumentException("Image must be a .png, .jpg, .jpeg or .webp file.", nameof(imagePath)) };
        if (new FileInfo(imagePath).Length > MaxImageBytes) throw new ArgumentException("Image is larger than 20 MB.", nameof(imagePath));
        var dataUri = $"data:{mime};base64,{Convert.ToBase64String(await File.ReadAllBytesAsync(imagePath, ct))}";
        var id = await CreateAsync("/openapi/v1/image-to-3d", new { image_url = dataUri }, key, ct);
        using var doc = await WaitAsync("/openapi/v1/image-to-3d", id, key, progress, ct);
        return await DownloadModelAsync(doc.RootElement, Path.GetFileNameWithoutExtension(imagePath), outputDir, ct);
    }

    public async Task<string> TextTo3DAsync(string prompt, string outputDir, Action<string>? progress, CancellationToken ct)
    {
        var key = Key(); if (string.IsNullOrWhiteSpace(prompt) || prompt.Length > 800) throw new ArgumentException("Prompt must be 1-800 characters.", nameof(prompt));
        var id = await CreateAsync("/openapi/v2/text-to-3d", new { mode = "preview", prompt }, key, ct);
        using var doc = await WaitAsync("/openapi/v2/text-to-3d", id, key, progress, ct);
        return await DownloadModelAsync(doc.RootElement, prompt, outputDir, ct);
    }

    private string Key()
    {
        var key = apiKey();
        if (string.IsNullOrWhiteSpace(key)) throw new InvalidOperationException("Meshy API key not set. Add key id meshy in Settings.");
        return key;
    }

    private static HttpRequestMessage Make(HttpMethod method, string url, string key)
    {
        var req = new HttpRequestMessage(method, url);
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", key);
        return req;
    }

    private async Task<string> CreateAsync(string route, object payload, string key, CancellationToken ct)
    {
        using var req = Make(HttpMethod.Post, BaseUrl + route, key);
        req.Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
        using var response = await http.SendAsync(req, ct);
        using var doc = await ReadJsonAsync(response, ct);
        return doc.RootElement.TryGetProperty("result", out var id) && id.ValueKind == JsonValueKind.String ? id.GetString()! : throw new InvalidOperationException("Meshy did not return a task id.");
    }

    private async Task<JsonDocument> WaitAsync(string route, string id, string key, Action<string>? progress, CancellationToken ct)
    {
        var started = DateTimeOffset.UtcNow;
        while (true)
        {
            ct.ThrowIfCancellationRequested();
            using var req = Make(HttpMethod.Get, $"{BaseUrl}{route}/{Uri.EscapeDataString(id)}", key);
            using var response = await http.SendAsync(req, ct);
            var doc = await ReadJsonAsync(response, ct);
            var root = doc.RootElement;
            var status = root.TryGetProperty("status", out var s) ? s.GetString() : null;
            if (root.TryGetProperty("progress", out var p) && p.ValueKind == JsonValueKind.Number) progress?.Invoke($"Meshy {p.GetInt32()}%");
            if (status == "SUCCEEDED") return doc;
            if (status is "FAILED" or "CANCELED")
            {
                var message = TaskErrorMessage(root, status);
                doc.Dispose();
                throw new InvalidOperationException(message);
            }
            doc.Dispose();
            if (DateTimeOffset.UtcNow - started > OverallTimeout) throw new InvalidOperationException("Meshy task timed out.");
            await Task.Delay(PollInterval, ct);
        }
    }

    private static string TaskErrorMessage(JsonElement root, string status)
    {
        var detail = root.TryGetProperty("task_error", out var e) && e.ValueKind == JsonValueKind.Object && e.TryGetProperty("message", out var m) && m.ValueKind == JsonValueKind.String ? m.GetString() : null;
        return $"Meshy task {status.ToLowerInvariant()}: {(string.IsNullOrWhiteSpace(detail) ? "no details given" : detail)}";
    }

    private static async Task<JsonDocument> ReadJsonAsync(HttpResponseMessage response, CancellationToken ct)
    {
        if ((int)response.StatusCode == 429) throw new UsageLimitException(DateTimeOffset.UtcNow.AddHours(1));
        if (!response.IsSuccessStatusCode) throw new InvalidOperationException($"Meshy HTTP {(int)response.StatusCode}. Check the API key and request.");
        var text = await response.Content.ReadAsStringAsync(ct);
        if (text.Length > MaxJsonChars) throw new InvalidOperationException("Meshy response too large.");
        return JsonDocument.Parse(text);
    }

    private async Task<string> DownloadModelAsync(JsonElement root, string source, string outputDir, CancellationToken ct)
    {
        if (!root.TryGetProperty("model_urls", out var urls) || urls.ValueKind != JsonValueKind.Object || !urls.TryGetProperty("glb", out var glb) || glb.GetString() is not { Length: > 0 } url) throw new InvalidOperationException("Meshy finished without a GLB file.");
        return await DownloadAsync(url, outputDir, FileName(source, ".glb"), ct);
    }

    private async Task<string> DownloadAsync(string url, string outputDir, string fileName, CancellationToken ct)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps) throw new InvalidOperationException("Meshy returned a non-HTTPS download URL.");
        Directory.CreateDirectory(outputDir);
        var target = Path.Combine(outputDir, fileName); var temp = target + ".tmp";
        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Get, uri);
            using var response = await http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct);
            if (!response.IsSuccessStatusCode) throw new InvalidOperationException($"Meshy download failed with HTTP {(int)response.StatusCode}.");
            if (response.Content.Headers.ContentLength is > MaxDownloadBytes) throw new InvalidOperationException("Meshy download exceeds 200 MB.");
            await using (var input = await response.Content.ReadAsStreamAsync(ct))
            await using (var output = File.Create(temp))
            {
                var buffer = new byte[81920]; long total = 0; int read;
                while ((read = await input.ReadAsync(buffer, ct)) > 0)
                {
                    total += read; if (total > MaxDownloadBytes) throw new InvalidOperationException("Meshy download exceeds 200 MB.");
                    await output.WriteAsync(buffer.AsMemory(0, read), ct);
                }
            }
            File.Move(temp, target, true);
            return target;
        }
        catch
        {
            try { File.Delete(temp); } catch (IOException) { } catch (UnauthorizedAccessException) { }
            throw;
        }
    }

    private static string FileName(string source, string extension)
    {
        var slug = new StringBuilder();
        foreach (var c in source.ToLowerInvariant())
        {
            if (c is (>= 'a' and <= 'z') or (>= '0' and <= '9')) slug.Append(c);
            else if (slug.Length > 0 && slug[^1] != '-') slug.Append('-');
        }
        var text = slug.ToString().TrimEnd('-');
        if (text.Length > 40) text = text[..40].TrimEnd('-');
        if (text.Length == 0) text = "meshy";
        return $"{DateTime.UtcNow:yyyyMMdd-HHmmss-fff}-{text}{extension}";
    }
}
