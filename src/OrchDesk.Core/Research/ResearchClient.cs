using System.Net.Http.Headers;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using OrchDesk.Core.Config;
using OrchDesk.Core.Models;
using OrchDesk.Core.Providers;

namespace OrchDesk.Core.Research;

// Research page back end (from the original build): Gemini answers questions, optionally grounded in text the user pasted from a
// NotebookLM notebook. The key is the "gemini" entry in secrets.bin (or the gemini provider's saved key).
public sealed class ResearchClient(Func<string?> apiKey, HttpClient http, string baseUrl = LegacyDefaults.GeminiUrl)
{
    public const string KeyId = "gemini";
    public const int MaxContextChars = 60_000;
    private const string SystemPrompt = "You are the research assistant inside OrchDesk. Answer clearly and concisely in Markdown. When source material is supplied, base your answer on it and say when it does not contain the answer. Do not invent citations.";

    public bool Linked { get { try { return !string.IsNullOrEmpty(apiKey()); } catch (System.Security.Cryptography.CryptographicException) { return false; } } }

    // Google's list mixes chat models with speech, image, robotics and codename models, so only text-chat Gemini names are kept:
    // gemini-[version-]pro|flash[-lite][-preview][-latest]. Newest version first, "pro" before "flash", moving "-latest" aliases last.
    private static readonly Regex ChatModel = new(@"^gemini-(?:(?<v>\d+(?:\.\d+)?)-)?(?<tier>pro|flash)(?<lite>-lite)?(?:-preview)?(?<latest>-latest)?$", RegexOptions.Compiled);
    public static IReadOnlyList<string> Rank(IEnumerable<string> ids) => ids.Select(i => i.StartsWith("models/") ? i[7..] : i).Distinct()
        .Select(i => (Id: i, M: ChatModel.Match(i))).Where(x => x.M.Success)
        .OrderBy(x => x.M.Groups["latest"].Success)
        .ThenByDescending(x => x.M.Groups["v"].Success ? double.Parse(x.M.Groups["v"].Value, System.Globalization.CultureInfo.InvariantCulture) : 0)
        .ThenByDescending(x => x.M.Groups["tier"].Value == "pro").ThenBy(x => x.M.Groups["lite"].Success).ThenBy(x => x.Id).Select(x => x.Id).ToArray();
    // Default for research: the newest full flash model. Pro models are stronger but often have no free quota; flash answers reliably.
    public static string? Default(IReadOnlyList<string> ranked) => ranked.FirstOrDefault(i => i.Contains("flash") && !i.Contains("lite") && !i.EndsWith("-latest")) ?? ranked.FirstOrDefault();
    public static string Explain(Exception e) => e.Message switch
    {
        var m when m.Contains("HTTP 404") || m.Contains("404") => "Gemini says this model is retired or not enabled for your key. Pick another model from the list.",
        var m when m.Contains("400") || m.Contains("401") || m.Contains("403") => "Gemini rejected the key or request. Check the gemini key in Settings.",
        var m => "Could not reach Gemini: " + m
    };

    public async Task<IReadOnlyList<string>> ModelsAsync(CancellationToken ct = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, baseUrl.TrimEnd('/') + "/models");
        if (apiKey() is { Length: > 0 } key) request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", key);
        using var response = await http.SendAsync(request, ct);
        if (!response.IsSuccessStatusCode) throw new InvalidOperationException($"Provider HTTP {(int)response.StatusCode}.");
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
        return Rank(doc.RootElement.GetProperty("data").EnumerateArray().Select(e => e.GetProperty("id").GetString()!).ToArray());
    }

    public static List<ChatMessage> Build(string question, string? sourceTitle, string? sourceText, IReadOnlyList<ChatMessage> history)
    {
        var messages = new List<ChatMessage> { ChatMessage.System(SystemPrompt) };
        messages.AddRange(history.TakeLast(12));
        var prompt = new StringBuilder();
        if (!string.IsNullOrWhiteSpace(sourceText))
        {
            var text = sourceText.Length > MaxContextChars ? sourceText[..MaxContextChars] + "\n[source truncated]" : sourceText;
            prompt.Append("Source material from the notebook \"").Append(string.IsNullOrWhiteSpace(sourceTitle) ? "untitled" : sourceTitle).Append("\":\n\"\"\"\n").Append(text).Append("\n\"\"\"\n\n");
        }
        messages.Add(ChatMessage.User(prompt.Append(question).ToString()));
        return messages;
    }

    public async IAsyncEnumerable<string> AskAsync(string model, IReadOnlyList<ChatMessage> messages, [EnumeratorCancellation] CancellationToken ct = default)
    {
        if (!Linked) throw new InvalidOperationException("Link Gemini first: save your AI Studio key under the gemini key id in Settings.");
        var provider = new OpenAiCompatProvider("gemini", "Gemini", new OpenAiCompatOptions(baseUrl, model, apiKey()), false, http);
        await foreach (var e in provider.StreamChatAsync(new ChatRequest(messages, []), ct))
        {
            if (e is TextDelta d) yield return d.Text;
            else if (e is ErrorEvent err) throw new InvalidOperationException(err.Message);
            else if (e is ProviderUnavailable p) throw new InvalidOperationException(p.Message);
            else if (e is UsageLimitHit u) throw new InvalidOperationException(u.Message);
        }
    }
}
