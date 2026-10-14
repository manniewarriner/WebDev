using System.Net;
using System.Net.Http.Headers;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json.Nodes;
using OrchDesk.Core.Models;

namespace OrchDesk.Core.Providers;

public sealed record OpenAiCompatOptions(
    string BaseUrl,
    string Model,
    string? ApiKey = null,
    IReadOnlyDictionary<string, string>? ExtraHeaders = null,
    double? Temperature = null);

/// <summary>
/// Any OpenAI-compatible chat endpoint: local PC models (Ollama, LM Studio, llama.cpp), OpenCode Zen/Go,
/// or the Cloudflare relay to the PC (Access service-token headers go in ExtraHeaders).
/// OrchDesk's AgentLoop executes the tool calls, so IsAgentic is false.
/// </summary>
public sealed class OpenAiCompatProvider : IModelProvider
{
    private readonly HttpClient _http;
    private readonly OpenAiCompatOptions _options;

    public OpenAiCompatProvider(string id, string displayName, OpenAiCompatOptions options, bool isLocal, HttpClient http)
    {
        Id = id;
        DisplayName = displayName;
        IsLocal = isLocal;
        _options = options;
        _http = http;
    }

    public string Id { get; }
    public string DisplayName { get; }
    public bool IsAgentic => false;
    public bool IsLocal { get; }

    private string Url(string path) => _options.BaseUrl.TrimEnd('/') + path;

    private void AddHeaders(HttpRequestMessage req)
    {
        if (!string.IsNullOrEmpty(_options.ApiKey))
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _options.ApiKey);
        if (_options.ExtraHeaders is not null)
            foreach (var (k, v) in _options.ExtraHeaders) req.Headers.TryAddWithoutValidation(k, v);
    }

    public async Task<ProviderHealth> CheckHealthAsync(CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(_options.BaseUrl) || string.IsNullOrWhiteSpace(_options.Model))
            return ProviderHealth.NotConfigured;
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(TimeSpan.FromSeconds(4));
            using var req = new HttpRequestMessage(HttpMethod.Get, Url("/models"));
            AddHeaders(req);
            using var resp = await _http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
            // Access login pages can return HTTP 200; they are not a working model endpoint.
            return resp.IsSuccessStatusCode && resp.Content.Headers.ContentType?.MediaType != "text/html"
                ? ProviderHealth.Online : ProviderHealth.Offline;
        }
        catch (Exception) when (!ct.IsCancellationRequested)
        {
            return ProviderHealth.Offline;
        }
    }

    public static JsonObject BuildRequestBody(OpenAiCompatOptions options, ChatRequest request)
    {
        var messages = new JsonArray();
        foreach (var m in request.Messages)
        {
            var obj = new JsonObject { ["role"] = m.Role.ToString().ToLowerInvariant() };
            if (m.Role == ChatRole.Tool)
            {
                obj["tool_call_id"] = m.ToolCallId;
                obj["content"] = m.Content ?? string.Empty;
            }
            else
            {
                obj["content"] = m.Content;
                if (m.ToolCalls is { Count: > 0 })
                {
                    obj["tool_calls"] = new JsonArray(m.ToolCalls.Select(c => (JsonNode)new JsonObject
                    {
                        ["id"] = c.Id,
                        ["type"] = "function",
                        ["function"] = new JsonObject { ["name"] = c.Name, ["arguments"] = c.ArgumentsJson },
                    }).ToArray());
                }
            }
            messages.Add(obj);
        }

        var body = new JsonObject
        {
            ["model"] = options.Model,
            ["messages"] = messages,
            ["stream"] = true,
        };
        if (options.Temperature is double t) body["temperature"] = t;
        if (request.Tools.Count > 0)
        {
            body["tools"] = new JsonArray(request.Tools.Select(tool => (JsonNode)new JsonObject
            {
                ["type"] = "function",
                ["function"] = new JsonObject
                {
                    ["name"] = tool.Name,
                    ["description"] = tool.Description,
                    ["parameters"] = tool.Parameters.DeepClone(),
                },
            }).ToArray());
        }
        return body;
    }

    public async IAsyncEnumerable<ChatEvent> StreamChatAsync(ChatRequest request, [EnumeratorCancellation] CancellationToken ct)
    {
        var body = BuildRequestBody(_options, request);
        using var req = new HttpRequestMessage(HttpMethod.Post, Url("/chat/completions"))
        {
            Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json"),
        };
        AddHeaders(req);

        // C# doesn't allow "yield" inside a try/catch, so the failure is captured and reported afterwards.
        HttpResponseMessage? resp = null;
        ChatEvent? failure = null;
        try
        {
            resp = await _http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct);
        }
        catch (HttpRequestException ex)
        {
            failure = new ProviderUnavailable(Id, $"{DisplayName} is unreachable: {ex.Message}");
        }
        catch (TaskCanceledException) when (!ct.IsCancellationRequested)
        {
            failure = new ProviderUnavailable(Id, $"{DisplayName} timed out.");
        }

        if (failure is not null) { yield return failure; yield break; }

        using (resp)
        {
            if (!resp!.IsSuccessStatusCode)
            {
                var text = await resp.Content.ReadAsStringAsync(ct);
                if (resp.StatusCode is HttpStatusCode.TooManyRequests or HttpStatusCode.PaymentRequired)
                    yield return new UsageLimitHit(Id, $"{(int)resp.StatusCode}: {Truncate(text)}");
                else if ((int)resp.StatusCode >= 500 || resp.StatusCode is HttpStatusCode.BadGateway)
                    yield return new ProviderUnavailable(Id, $"{(int)resp.StatusCode}: {Truncate(text)}");
                else
                    yield return new ErrorEvent($"{DisplayName} returned {(int)resp.StatusCode}: {Truncate(text)}");
                yield break;
            }

            // A Cloudflare Access login page comes back as 200 text/html: the PC relay isn't really reachable.
            if (resp.Content.Headers.ContentType?.MediaType == "text/html")
            {
                yield return new ProviderUnavailable(Id, $"{DisplayName} returned an HTML page (relay/login), treating as offline.");
                yield break;
            }

            if (resp.Content.Headers.ContentType?.MediaType == "application/json")
            {
                var json = await resp.Content.ReadAsStringAsync(ct);
                foreach (var ev in SseChatParser.ParseCompletion(json)) yield return ev;
                yield break;
            }
            await using var stream = await resp.Content.ReadAsStreamAsync(ct);
            using var reader = new StreamReader(stream);
            await foreach (var ev in SseChatParser.ParseAsync(ReadLines(reader, ct), ct))
                yield return ev;
        }
    }

    private static async IAsyncEnumerable<string> ReadLines(StreamReader reader, [EnumeratorCancellation] CancellationToken ct)
    {
        while (await reader.ReadLineAsync(ct) is { } line) yield return line;
    }

    private static string Truncate(string s) => s.Length <= 500 ? s : s[..500] + "…";
}
