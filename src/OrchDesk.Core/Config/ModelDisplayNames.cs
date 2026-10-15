using System.Globalization;
using System.Text.RegularExpressions;
namespace OrchDesk.Core.Config;

public static class ModelDisplayNames
{
    public static string Get(string? id)
    {
        if (string.IsNullOrWhiteSpace(id)) return "Automatic";
        var name = id.Split('/').Last();
        foreach (var tier in new[] { "sonnet", "opus", "haiku" })
            if (name.Contains(tier, StringComparison.OrdinalIgnoreCase)) return "Claude " + CultureInfo.InvariantCulture.TextInfo.ToTitleCase(tier);
        name = Regex.Replace(name, @"-qat$", "", RegexOptions.IgnoreCase);
        name = Regex.Replace(name, @"^(qwen|gemma|glm)(?=\d)", "$1 ", RegexOptions.IgnoreCase);
        name = Regex.Replace(name, @"[-_]", " ");
        name = CultureInfo.InvariantCulture.TextInfo.ToTitleCase(name.ToLowerInvariant());
        name = Regex.Replace(name, @"\bGlm\b", "GLM");
        name = Regex.Replace(name, @"(\d)\.X\b", "$1.x");
        return Regex.Replace(name, @"(\d+)b\b", "$1B", RegexOptions.IgnoreCase);
    }
}
