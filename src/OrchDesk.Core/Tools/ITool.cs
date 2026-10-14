using System.Text.Json.Nodes;
using OrchDesk.Core.Models;

namespace OrchDesk.Core.Tools;

public sealed class ToolContext(string workspaceRoot, HttpClient? http = null)
{
    public string WorkspaceRoot { get; } = WorkspacePath.NormalizeRoot(workspaceRoot);
    public HttpClient Http { get; } = http ?? SharedHttp.Instance;
    public TimeSpan DefaultCommandTimeout { get; init; } = TimeSpan.FromMinutes(2);
}

internal static class SharedHttp
{
    public static readonly HttpClient Instance = new() { Timeout = TimeSpan.FromSeconds(30) };
}

public sealed class ToolException(string message) : Exception(message);

public interface ITool
{
    string Name { get; }
    string Description { get; }
    JsonObject Parameters { get; }
    Task<string> ExecuteAsync(JsonObject args, ToolContext ctx, CancellationToken ct);
}

public static class ToolArgs
{
    public static string Str(JsonObject a, string name) =>
        a[name]?.GetValue<string>() ?? throw new ToolException($"Missing required argument '{name}'.");
    public static string? OptStr(JsonObject a, string name) =>
        a[name] is JsonValue v && v.TryGetValue<string>(out var s) ? s : null;
    public static int? OptInt(JsonObject a, string name) =>
        a[name] is JsonValue v ? (v.TryGetValue<int>(out var i) ? i : v.TryGetValue<string>(out var s) && int.TryParse(s, out var j) ? j : null) : null;
    public static bool OptBool(JsonObject a, string name) =>
        a[name] is JsonValue v && v.TryGetValue<bool>(out var b) && b;

    /// <summary>Small helper to write JSON Schemas for tool parameters.</summary>
    public static JsonObject Schema(params (string Name, string Type, string Description, bool Required)[] props)
    {
        var properties = new JsonObject();
        foreach (var p in props) properties[p.Name] = new JsonObject { ["type"] = p.Type, ["description"] = p.Description };
        return new JsonObject
        {
            ["type"] = "object",
            ["properties"] = properties,
            ["required"] = new JsonArray(props.Where(p => p.Required).Select(p => (JsonNode)p.Name).ToArray()),
        };
    }
}

public sealed class ToolRegistry
{
    private readonly Dictionary<string, ITool> _tools = new(StringComparer.Ordinal);

    public ToolRegistry Add(ITool tool) { _tools[tool.Name] = tool; return this; }
    public ITool? Get(string name) => _tools.GetValueOrDefault(name);
    public IReadOnlyList<ToolDef> Definitions => _tools.Values.Select(t => new ToolDef(t.Name, t.Description, t.Parameters)).ToList();

    public static ToolRegistry CreateDefault() => new ToolRegistry()
        .Add(new ListFilesTool()).Add(new ReadFileTool()).Add(new SearchTool())
        .Add(new EditFileTool()).Add(new WriteFileTool()).Add(new DeleteFileTool())
        .Add(new RunCommandTool())
        .Add(new GitTool("git_status", "Show git status (short format) of the project.", ["status", "--short", "--branch"]))
        .Add(new GitDiffTool()).Add(new GitCommitTool())
        .Add(new GitTool("git_push", "Push the current branch to its remote. Always requires user approval.", ["push", "-u", "origin", "HEAD"]))
        .Add(new WebFetchTool());
}
