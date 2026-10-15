namespace OrchDesk.Core.Config;

/// <summary>Approximate relative UI hints, not benchmarks or billing quotes. Cost: higher means more expensive.</summary>
public sealed record ModelProfile(string Family, double Speed, double Cost, double Context, string ContextLabel) { public bool IsFree => Cost == 0; public bool IsLocal => ContextLabel.Contains("local", StringComparison.OrdinalIgnoreCase); }

public static class ModelProfiles
{
    public static IReadOnlyList<ModelProfile> Table { get; } = Array.AsReadOnly(new[] {
        new ModelProfile("Sonnet", .75, .6, .8, "200K"),
        new ModelProfile("Opus", .45, .95, .8, "200K"),
        new ModelProfile("Haiku", .95, .25, .8, "200K"),
        new ModelProfile("Qwen", .65, 0, .5, "128K · local"),
        new ModelProfile("Gemma 4 E2B", .95, 0, .5, "128K \u00b7 local"),
        new ModelProfile("Gemma", .6, 0, .5, "128K · local"),
        new ModelProfile("Bonsai", .5, 0, .5, "Local"),
        new ModelProfile("Nemotron", .85, 0, .5, "Local"),
        new ModelProfile("GLM", .7, .3, .8, "200K"),
        new ModelProfile("Kimi", .6, .35, 1, "256K"),
        new ModelProfile("Minimax", .85, .2, .8, "200K")
    });
    public static ModelProfile Unknown { get; } = new("Unknown", .5, .5, .5, "Not specified");
    public static ModelProfile Get(string? model) => Table.FirstOrDefault(p =>
        model?.Replace('-', ' ').Contains(p.Family, StringComparison.OrdinalIgnoreCase) == true) ?? Unknown;
}
