namespace OrchDesk.Core.Config;

public sealed record SendOptions(string Text, string? ProviderId, string? ConfigPath)
{
    public bool Diag { get; init; }
    public IReadOnlyList<string> Texts { get; init; } = [Text];
    public static SendOptions? Parse(string[] args)
    {
        if (!args.Contains("--send", StringComparer.OrdinalIgnoreCase)) return null;
        string? Value(string name, bool required = false)
        {
            var i = Array.FindIndex(args, a => a.Equals(name, StringComparison.OrdinalIgnoreCase));
            if (i < 0 && !required) return null;
            if (i < 0 || i + 1 >= args.Length || args[i + 1].StartsWith("--") || string.IsNullOrWhiteSpace(args[i + 1]))
                throw new ArgumentException($"{name} requires a value.");
            if (args.Count(a => a.Equals(name, StringComparison.OrdinalIgnoreCase)) > 1)
                throw new ArgumentException($"Duplicate {name}.");
            return args[i + 1];
        }
        var texts = new List<string>();
        for (var i = 0; i < args.Length; i++)
            if (args[i].Equals("--send", StringComparison.OrdinalIgnoreCase)) {
                if (++i >= args.Length || args[i].StartsWith("--") || string.IsNullOrWhiteSpace(args[i])) throw new ArgumentException("--send requires a value.");
                texts.Add(args[i]);
            }
        return new(texts[0], Value("--provider"), Value("--config")) { Texts = texts, Diag = args.Contains("--diag", StringComparer.OrdinalIgnoreCase) };
    }
}
