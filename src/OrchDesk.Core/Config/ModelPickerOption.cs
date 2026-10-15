namespace OrchDesk.Core.Config;

public sealed record ModelPickerOption(string? Id, string Label, string? Model = null)
{
    public string ProviderGroup => Id is null ? "Automatic" : Label.Split(" · ")[0];
    public string ModelName => Model ?? Label;
    public static List<ModelPickerOption> Expand(IEnumerable<ProviderConfig> providers) => providers.Where(p => p.Enabled)
        .SelectMany(p => p.AvailableModels.Count > 0
            ? p.AvailableModels.Distinct().Select(m => new ModelPickerOption(p.Id, $"{p.DisplayName} · {m}", m))
            : [new ModelPickerOption(p.Id, p.DisplayName)]).ToList();
}
