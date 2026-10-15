namespace OrchDesk.Core.Config;

public sealed record ModelPickerOption(string? Id, string Label, string? Model = null)
{
    public string ProviderGroup => Id is null ? "Automatic" : Label.Split(" · ")[0];
    public ProviderKind Kind { get; init; }
    public string? DisplayModel { get; init; }
    public string ModelName => Model is null && DisplayModel is null ? Label : ModelDisplayNames.Get(Model ?? DisplayModel);
    public static List<ModelPickerOption> Expand(IEnumerable<ProviderConfig> providers)
    {
        var enabled = providers.Where(p => p.Enabled &&
            !(p.Kind == ProviderKind.ClaudeCli && p.AvailableModels.Count == 0) &&
            !(p.Id.StartsWith("opencode", StringComparison.OrdinalIgnoreCase) && string.IsNullOrWhiteSpace(p.ApiKeyProtected))).ToList();
        var seen = new HashSet<(string, string)>();
        var options = new List<ModelPickerOption>();
        foreach (var p in enabled)
        {
            var endpoint = p.BaseUrl?.TrimEnd('/') ?? "";
            var shared = p.Kind == ProviderKind.OpenAiCompatible && endpoint.Length > 0;
            var group = shared ? enabled.First(q => q.Kind == p.Kind && q.BaseUrl?.TrimEnd('/') == endpoint) : p;
            var label = group.Id is "local-pc" or "local-pc-gemma" ? "LM Studio (PC)" : group.DisplayName;
            foreach (var model in p.AvailableModels.Count > 0 ? p.AvailableModels : new List<string> { p.Model ?? "" })
            {
                if (model.Contains("embed", StringComparison.OrdinalIgnoreCase) || !seen.Add((shared ? endpoint : p.Id, model))) continue;
                options.Add(p.AvailableModels.Count > 0
                    ? new(p.Id, $"{label} \u00b7 {ModelDisplayNames.Get(model)}", model) { Kind = p.Kind }
                    : new(p.Id, label) { Kind = p.Kind, DisplayModel = model });
            }
        }
        return options;
    }
}
