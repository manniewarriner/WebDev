using System.Runtime.CompilerServices;
using OrchDesk.Core.Models;
using OrchDesk.Core.Providers;

namespace OrchDesk.Tests;

/// <summary>A model that replays scripted turns, so the agent loop can be tested without any real LLM.</summary>
public sealed class FakeProvider(string id, params Func<ChatRequest, IEnumerable<ChatEvent>>[] turns) : IModelProvider
{
    private int _turn;
    public List<ChatRequest> Requests { get; } = [];
    public string Id { get; } = id;
    public string DisplayName => Id;
    public bool IsAgentic { get; init; }
    public bool IsLocal { get; init; }
    public ProviderHealth Health { get; set; } = ProviderHealth.Online;

    public Task<ProviderHealth> CheckHealthAsync(CancellationToken ct) => Task.FromResult(Health);

    public async IAsyncEnumerable<ChatEvent> StreamChatAsync(ChatRequest request, [EnumeratorCancellation] CancellationToken ct)
    {
        Requests.Add(request with { Messages = request.Messages.ToList() });
        var script = turns[Math.Min(_turn++, turns.Length - 1)];
        foreach (var ev in script(request))
        {
            ct.ThrowIfCancellationRequested();
            await Task.Yield();
            yield return ev;
        }
    }

    public static IEnumerable<ChatEvent> Say(string text) => [new TextDelta(text), new Done()];
    public static IEnumerable<ChatEvent> Call(string name, string json, string id = "c1") =>
        [new ToolCallRequested(new ToolCall(id, name, json)), new Done()];
}

public sealed class TempDir : IDisposable
{
    public string Path { get; } = Directory.CreateTempSubdirectory("orchdesk-test-").FullName;
    public string File(string rel, string content)
    {
        var full = System.IO.Path.Combine(Path, rel);
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(full)!);
        System.IO.File.WriteAllText(full, content);
        return full;
    }
    public void Dispose() { try { Directory.Delete(Path, true); } catch { } }
}

public static class AsyncExt
{
    public static async Task<List<T>> ToListAsync<T>(this IAsyncEnumerable<T> source)
    {
        var list = new List<T>();
        await foreach (var x in source) list.Add(x);
        return list;
    }
}
