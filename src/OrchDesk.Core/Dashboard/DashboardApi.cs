using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace OrchDesk.Core.Dashboard;

// The task and report API. It listens on 127.0.0.1 only, and every request needs `Authorization: Bearer <key>`.
// The routing lives in Handle so it can be tested without opening a socket.
public sealed class DashboardApi(TaskStore tasks, ReportStore reports, Func<string?> key, Func<DateTime>? clock = null)
{
    public const int MaxBodyBytes = 65536;
    private readonly Func<DateTime> now = clock ?? (() => DateTime.UtcNow);
    private static readonly JsonSerializerOptions Opts = new(JsonSerializerDefaults.Web) { DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull };

    public sealed record Result(int Status, string Body);

    public Result Handle(string method, string path, string query, string? authorization, string body)
    {
        var expected = key();
        if (string.IsNullOrEmpty(expected)) return Error(503, "The local API has no key yet. Press Generate key in Tasks first.");
        if (!Authorized(authorization, expected)) return Error(401, "Missing or wrong key. Send Authorization: Bearer <key>.");
        if (body.Length > MaxBodyBytes) return Error(413, "Request too large.");
        try { return Route(method.ToUpperInvariant(), path.TrimEnd('/'), ParseQuery(query), body); }
        catch (KeyNotFoundException e) { return Error(404, e.Message); }
        catch (ArgumentException e) { return Error(400, e.Message); }
        catch (JsonException) { return Error(400, "Body must be valid JSON."); }
        catch (InvalidOperationException e) { return Error(409, e.Message); }
    }

    private Result Route(string method, string path, Dictionary<string, string> q, string body)
    {
        using var doc = JsonDocument.Parse(string.IsNullOrWhiteSpace(body) ? "{}" : body);
        var root = doc.RootElement;
        if (root.ValueKind != JsonValueKind.Object) throw new ArgumentException("Body must be a JSON object.");

        if (path == "/api/tasks")
        {
            if (method == "GET") return TaskList(q);
            if (method == "POST")
            {
                var t = tasks.Add(Str(root, "title") ?? "", Str(root, "project") ?? "", Str(root, "priority") ?? "P2", Str(root, "due"), Str(root, "notes") ?? "", "api");
                return Ok(201, t);
            }
        }
        if (path.StartsWith("/api/tasks/", StringComparison.Ordinal))
        {
            var id = path["/api/tasks/".Length..];
            if (method == "PATCH") return Ok(200, tasks.Update(id, Patch(root)));
            if (method == "DELETE") return tasks.Delete(id) ? Ok(200, new { deleted = id }) : Error(404, "No task with that id.");
            if (method == "GET") return Ok(200, tasks.Get(id) ?? throw new KeyNotFoundException("No task with that id."));
        }
        if (path == "/api/reports")
        {
            if (method == "GET") return ReportList(q);
            if (method == "POST")
            {
                var r = reports.Add(Str(root, "source") ?? "", Str(root, "type") ?? "update", Str(root, "title") ?? "", Str(root, "body") ?? "", RawData(root), Str(root, "date"), now());
                return Ok(201, ReportView(r));
            }
        }
        if (path.StartsWith("/api/reports/", StringComparison.Ordinal))
        {
            var id = path["/api/reports/".Length..];
            if (method == "GET") return Ok(200, ReportView(reports.Get(id) ?? throw new KeyNotFoundException("No report with that id.")));
            if (method == "DELETE") return reports.Delete(id) ? Ok(200, new { deleted = id }) : Error(404, "No report with that id.");
        }
        return Error(404, "No such route. See README for the API.");
    }

    private Result TaskList(Dictionary<string, string> q)
    {
        if (q.TryGetValue("view", out var view) && view == "today")
        {
            var today = TaskRules.LondonDate(now());
            var v = TaskRules.Today(tasks.List(), today);
            return Ok(200, new { date = today, overdue = v.Overdue, dueToday = v.DueToday, inProgress = v.InProgress });
        }
        q.TryGetValue("project", out var project);
        q.TryGetValue("status", out var status);
        return Ok(200, new { tasks = tasks.List(string.IsNullOrEmpty(project) ? null : project, string.IsNullOrEmpty(status) ? null : TaskRules.NormalizeStatus(status)) });
    }

    private Result ReportList(Dictionary<string, string> q)
    {
        q.TryGetValue("source", out var source); q.TryGetValue("type", out var type); q.TryGetValue("limit", out var limitText);
        int limit = int.TryParse(limitText, out var n) ? n : 50;
        return Ok(200, new { reports = reports.List(string.IsNullOrEmpty(source) ? null : source, string.IsNullOrEmpty(type) ? null : type, limit).Select(ReportView).ToList() });
    }

    // PATCH: a missing field is left alone; "due": null or "" clears the due date.
    private static TaskPatch Patch(JsonElement root)
    {
        bool clear = root.TryGetProperty("due", out var due) && (due.ValueKind == JsonValueKind.Null || (due.ValueKind == JsonValueKind.String && due.GetString() == ""));
        return new TaskPatch(Str(root, "title"), Str(root, "project"), Str(root, "priority"), clear ? null : Str(root, "due"), clear, Str(root, "status"), Str(root, "notes"));
    }

    private static string? RawData(JsonElement root)
    {
        if (!root.TryGetProperty("data", out var data) || data.ValueKind == JsonValueKind.Null) return null;
        if (data.ValueKind != JsonValueKind.Object) throw new ArgumentException("data must be a JSON object.");
        return data.GetRawText();
    }

    private static object ReportView(ReportItem r) => new
    {
        r.Id, r.Source, r.Type, r.Title, r.Body, r.Date, receivedAt = r.ReceivedAt,
        data = r.DataJson == null ? null : JsonNode.Parse(r.DataJson),
    };

    private static string? Str(JsonElement root, string name) => root.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    private static Dictionary<string, string> ParseQuery(string query)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var part in query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var kv = part.Split('=', 2);
            result[Uri.UnescapeDataString(kv[0])] = kv.Length > 1 ? Uri.UnescapeDataString(kv[1].Replace('+', ' ')) : "";
        }
        return result;
    }

    // Compare SHA-256 digests in constant time, so the key's length and content cannot be probed by timing.
    public static bool Authorized(string? header, string expected)
    {
        if (header == null || !header.StartsWith("Bearer ", StringComparison.Ordinal)) return false;
        var given = SHA256.HashData(Encoding.UTF8.GetBytes(header[7..].Trim()));
        var want = SHA256.HashData(Encoding.UTF8.GetBytes(expected));
        return CryptographicOperations.FixedTimeEquals(given, want);
    }

    public static string NewKey() => Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static Result Ok(int status, object value) => new(status, JsonSerializer.Serialize(value, Opts));
    private static Result Error(int status, string message) => new(status, JsonSerializer.Serialize(new { error = message }, Opts));
}

// Listens on loopback only, and serves only /api/ paths.
public sealed class LocalApiHost(DashboardApi api) : IDisposable
{
    private HttpListener? listener;
    private CancellationTokenSource? cts;
    public int? Port { get; private set; }
    public string? Problem { get; private set; }

    public void Start(int port)
    {
        Stop();
        try
        {
            var l = new HttpListener();
            l.Prefixes.Add($"http://127.0.0.1:{port}/api/");
            l.Start();
            listener = l; Port = port; Problem = null;
            cts = new CancellationTokenSource();
            _ = Task.Run(() => Accept(l, cts.Token));
        }
        catch (Exception e) when (e is HttpListenerException or ObjectDisposedException or InvalidOperationException)
        {
            Problem = $"Could not open port {port}: {e.Message}. Pick another port in Settings.";
            Port = null;
        }
    }

    private async Task Accept(HttpListener l, CancellationToken ct)
    {
        while (!ct.IsCancellationRequested && l.IsListening)
        {
            HttpListenerContext ctx;
            try { ctx = await l.GetContextAsync(); }
            catch (Exception e) when (e is HttpListenerException or ObjectDisposedException or InvalidOperationException) { break; }
            _ = Task.Run(() => Serve(ctx));
        }
    }

    private void Serve(HttpListenerContext ctx)
    {
        try
        {
            var req = ctx.Request;
            if (req.ContentLength64 > DashboardApi.MaxBodyBytes) { Write(ctx, 413, "{\"error\":\"Request too large.\"}"); return; }
            string body;
            using (var reader = new StreamReader(req.InputStream, Encoding.UTF8)) {
                var buffer = new char[DashboardApi.MaxBodyBytes + 1];
                var count = reader.ReadBlock(buffer, 0, buffer.Length);
                body = new string(buffer, 0, count);
            }
            if (body.Length > DashboardApi.MaxBodyBytes) { Write(ctx, 413, "{\"error\":\"Request too large.\"}"); return; }
            var url = req.Url ?? throw new InvalidOperationException("No URL.");
            var result = api.Handle(req.HttpMethod, url.AbsolutePath, url.Query, req.Headers["Authorization"], body);
            Write(ctx, result.Status, result.Body);
        }
        catch (Exception) { try { ctx.Response.StatusCode = 500; } catch (ObjectDisposedException) { } }
        finally { try { ctx.Response.Close(); } catch (ObjectDisposedException) { } }
    }

    private static void Write(HttpListenerContext ctx, int status, string json)
    {
        var bytes = Encoding.UTF8.GetBytes(json);
        ctx.Response.StatusCode = status;
        ctx.Response.ContentType = "application/json; charset=utf-8";
        ctx.Response.ContentLength64 = bytes.Length;
        ctx.Response.OutputStream.Write(bytes, 0, bytes.Length);
    }

    public void Stop()
    {
        cts?.Cancel(); cts = null;
        try { listener?.Stop(); listener?.Close(); } catch (ObjectDisposedException) { }
        listener = null; Port = null;
    }

    public void Dispose() => Stop();
}
