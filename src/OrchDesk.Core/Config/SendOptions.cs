namespace OrchDesk.Core.Config;

public sealed record SendOptions(string Text, string? ProviderId, string? ConfigPath)
{
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
        return new(Value("--send", true)!, Value("--provider"), Value("--config"));
    }
}
