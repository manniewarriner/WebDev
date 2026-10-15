namespace OrchDesk.Core.Config;

public sealed record ModelPickerOption(string? Id, string Label, string? Model = null)
{
    public string ProviderGroup => Id is null ? "Automatic" : Label.Split(" · ")[0];
    public ProviderKind Kind { get; init; }
    public string? DisplayModel { get; init; }
    public string ModelName => Model is null && DisplayModel is null ? Label : ModelDisplayNames.Get(Model ?? DisplayModel);
    public static List<ModelPickerOption> Expand(IEnumerable<ProviderConfig> providers) => providers.Where(p => p.Enabled)
        .SelectMany(p => p.AvailableModels.Count > 0
            ? p.AvailableModels.Distinct().Select(m => new ModelPickerOption(p.Id, $"{p.DisplayName} · {ModelDisplayNames.Get(m)}", m) { Kind = p.Kind })
            : [new ModelPickerOption(p.Id, p.DisplayName) { Kind = p.Kind, DisplayModel = p.Model }]).ToList();
}
