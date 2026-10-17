using System.Text.Json.Nodes;
using OrchDesk.Core.Knowledge;

namespace OrchDesk.Core.Tools;

/// <summary>Saves one short fact to persistent memory (memory.json). The permission policy always asks before it runs.</summary>
public sealed class RememberTool(MemoryStore memory) : ITool
{
    public string Name => "remember";
    public string Description => "Save one short fact or preference (max 400 characters, no instructions, no secrets) to persistent memory. scope is project (default) or global. Needs the user's approval.";
    public JsonObject Parameters => ToolArgs.Schema(("text", "string", "The fact to remember", true), ("scope", "string", "project or global", false));
    public Task<string> ExecuteAsync(JsonObject args, ToolContext ctx, CancellationToken ct)
    {
        try { return Task.FromResult("Remembered: " + memory.Add(ctx.WorkspaceRoot, ToolArgs.Str(args, "text"), ToolArgs.OptStr(args, "scope") == "global").Text); }
        catch (ArgumentException e) { throw new ToolException(e.Message); }
    }
}

/// <summary>Meshy image generation (spends Meshy credits, so it always needs approval). Saves under assets/ in the project.</summary>
public sealed class GenerateImageTool(MeshyClient meshy) : ITool
{
    public string Name => "generate_image";
    public string Description => "Create an image from a text prompt with Meshy; saves a PNG under assets/ and returns its path. Spends Meshy credits; needs approval.";
    public JsonObject Parameters => ToolArgs.Schema(("prompt", "string", "Detailed description of the image", true));
    public Task<string> ExecuteAsync(JsonObject args, ToolContext ctx, CancellationToken ct) => MeshyErrors.Wrap(async () =>
    {
        var saved = await meshy.TextToImageAsync(ToolArgs.Str(args, "prompt"), WorkspacePath.Resolve(ctx.WorkspaceRoot, "assets"), null, ct);
        return "Saved " + WorkspacePath.ToRelative(ctx.WorkspaceRoot, saved);
    });
}

/// <summary>Meshy 3D generation from a prompt, or from an existing project image via image_path. Always needs approval.</summary>
public sealed class Generate3DTool(MeshyClient meshy) : ITool
{
    public string Name => "generate_3d";
    public string Description => "Create a textured 3D model (GLB) with Meshy from a text prompt, or from an existing project image via image_path; saves under assets/. Spends Meshy credits; needs approval.";
    public JsonObject Parameters => ToolArgs.Schema(("prompt", "string", "Description of the model", false), ("image_path", "string", "Project-relative image to turn into a model", false));
    public Task<string> ExecuteAsync(JsonObject args, ToolContext ctx, CancellationToken ct) => MeshyErrors.Wrap(async () =>
    {
        var assets = WorkspacePath.Resolve(ctx.WorkspaceRoot, "assets");
        string saved;
        if (ToolArgs.OptStr(args, "image_path") is { Length: > 0 } image)
        {
            var path = WorkspacePath.Resolve(ctx.WorkspaceRoot, image);
            if (!File.Exists(path)) throw new ToolException("image_path must be an existing file inside the project.");
            saved = await meshy.ImageTo3DAsync(path, assets, null, ct);
        }
        else saved = await meshy.TextTo3DAsync(ToolArgs.Str(args, "prompt"), assets, null, ct);
        return "Saved " + WorkspacePath.ToRelative(ctx.WorkspaceRoot, saved);
    });
}

internal static class MeshyErrors
{
    // Meshy reports bad input as ArgumentException and quota as UsageLimitException; the agent loop expects ToolException.
    public static async Task<string> Wrap(Func<Task<string>> action)
    {
        try { return await action(); }
        catch (ArgumentException e) { throw new ToolException(e.Message); }
        catch (OrchDesk.Core.Automation.UsageLimitException e) { throw new ToolException($"Meshy usage limit reached; try again after {e.ResetAt.LocalDateTime:t}."); }
    }
}
