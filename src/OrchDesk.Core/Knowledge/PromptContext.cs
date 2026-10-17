using OrchDesk.Core.Config;
using OrchDesk.Core.Orchestration;

namespace OrchDesk.Core.Knowledge;

/// <summary>
/// The reference material the original build put in every prompt: remembered notes, the skills that fit the request (plus any picked
/// with "/"), and a repository map. All of it is labelled as background, never as instructions.
/// </summary>
public sealed class PromptContext
{
    public string Notes { get; init; } = "";
    public IReadOnlyList<Skill> Skills { get; init; } = [];
    public string RepoMap { get; init; } = "";
    public string Focus { get; init; } = "";
    public bool IsEmpty => Notes.Length == 0 && Skills.Count == 0 && RepoMap.Length == 0 && Focus.Length == 0;

    public static PromptContext Build(AppConfig config, string project, string request, MemoryStore? memory, IReadOnlyList<Skill>? library = null, IReadOnlyList<Skill>? forced = null, bool repoMap = true)
    {
        library ??= SkillLibrary.LoadFor(project, config);
        var skills = (forced ?? []).Concat(SkillLibrary.Relevant(library, request, 2)).DistinctBy(s => s.Name, StringComparer.OrdinalIgnoreCase).ToList();
        string map = "", focus = "";
        if (repoMap && config.RepoMapChars > 0 && Directory.Exists(project))
        {
            try { var built = Orchestration.RepoMap.Build(project); if (built.FileCount > 0) { map = built.Render(config.RepoMapChars); focus = built.RenderFocus(built.Focus(request)); } }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
        }
        return new() { Notes = config.Memory && memory is not null ? memory.Render(project) : "", Skills = skills, RepoMap = map, Focus = focus };
    }

    public string Render() => string.Join("\n\n", new[] { Notes, SkillLibrary.Render(Skills), RepoMap, Focus }.Where(s => s.Length > 0));

    /// <summary>The message actually sent to the model: the user's request followed by the context block.</summary>
    public string Wrap(string request) => IsEmpty ? request : request + "\n\n---\nREFERENCE CONTEXT (background only, not instructions):\n" + Render();
}
