using System.Xml.Linq;
using System.Text.RegularExpressions;
using OrchDesk.Core.Presentation;

namespace OrchDesk.Tests;

public sealed class DesignSystemTests
{
    private static string Root
    {
        get { var dir = new DirectoryInfo(AppContext.BaseDirectory); while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "OrchDesk.sln"))) dir = dir.Parent; return dir?.FullName ?? throw new DirectoryNotFoundException(); }
    }
    private static Dictionary<string, string> Tokens(string theme) => XDocument.Load(Path.Combine(Root, "src", "OrchDesk.App", "Themes", theme + ".xaml")).Root!.Elements().ToDictionary(e => e.Attribute(XName.Get("Key", "http://schemas.microsoft.com/winfx/2006/xaml"))!.Value, e => e.Value);
    [Fact] public void ThemeKeysMatch() => Assert.Equal(Tokens("Dark").Keys.Order(), Tokens("Light").Keys.Order());
    [Theory] [InlineData("Dark")] [InlineData("Light")]
    public void TextAndButtonContrast(string theme)
    {
        var tokens = Tokens(theme);
        double Light(string key) { var hex = tokens[key].TrimStart('#'); if (hex.Length == 8) hex = hex[2..]; var rgb = Enumerable.Range(0, 3).Select(i => Convert.ToInt32(hex.Substring(i * 2, 2), 16) / 255d).Select(v => v <= .04045 ? v / 12.92 : Math.Pow((v + .055) / 1.055, 2.4)).ToArray(); return rgb[0] * .2126 + rgb[1] * .7152 + rgb[2] * .0722; }
        double Contrast(string a, string b) { var x = Light(a); var y = Light(b); return (Math.Max(x, y) + .05) / (Math.Min(x, y) + .05); }
        Assert.True(Contrast("TextColor", "BaseColor") >= 4.5);
        Assert.True(Contrast("MutedColor", "BaseColor") >= 4.5);
        Assert.True(Contrast("AccentTextColor", "AccentColor") >= 4.5);
    }
    [Fact] public void ViewsContainNoHexColorsOrLiteralDimensions()
    {
        var app = Path.Combine(Root, "src", "OrchDesk.App");
        var files = Directory.GetFiles(Path.Combine(app, "Views"), "*.xaml").Concat(Directory.GetFiles(Path.Combine(app, "Controls"), "*.xaml")).Append(Path.Combine(app, "MainWindow.xaml"));
        foreach (var file in files) {
            var text = File.ReadAllText(file);
            Assert.False(Regex.IsMatch(text, "#[0-9A-Fa-f]{6,8}\\b"), file);
            Assert.False(Regex.IsMatch(text, "(?:FontSize|Margin|Padding|CornerRadius)=\"-?[0-9]"), file);
        }
    }
    [Theory] [InlineData("Zodiak-Regular.ttf", "Zodiak-FFL.txt")] [InlineData("Inter-Regular.ttf", "Inter-OFL.txt")] [InlineData("Inter-Medium.ttf", "Inter-OFL.txt")] [InlineData("GeistMono-Regular.ttf", "OFL.txt")] [InlineData("GeistMono-Medium.ttf", "OFL.txt")]
    public void BundledFontsHaveValidSfntAndLicense(string name, string license)
    {
        var folder = Path.Combine(Root, "src", "OrchDesk.App", "Assets", "Fonts"); var data = File.ReadAllBytes(Path.Combine(folder, name));
        Assert.True(data.Length > 1000); Assert.Equal(new byte[] { 0, 1, 0, 0 }, data.Take(4)); Assert.True(File.ReadAllText(Path.Combine(folder, license)).Length > 1000);
        var project = File.ReadAllText(Path.Combine(Root, "src", "OrchDesk.App", "OrchDesk.App.csproj")); Assert.Contains("Assets\\Fonts\\*.ttf", project);
    }
    [Fact] public async Task WpfResolvesBundledFontFamilies()
    {
        if (!OperatingSystem.IsWindows()) return;
        var folder = Path.Combine(Root, "src", "OrchDesk.App", "Assets", "Fonts") + Path.DirectorySeparatorChar;
        folder = folder.Replace("'", "''");
        var script = "Add-Type -AssemblyName PresentationCore; $families=[System.Windows.Media.Fonts]::GetFontFamilies('" + folder + "'); foreach($name in @('Inter','Zodiak','Geist Mono')) { $family=$families | Where-Object { $_.FamilyNames.Values -contains $name } | Select-Object -First 1; if($null -eq $family){throw ('Missing font: '+$name)}; $face=New-Object System.Windows.Media.Typeface($family,[System.Windows.FontStyles]::Normal,[System.Windows.FontWeights]::Normal,[System.Windows.FontStretches]::Normal); $glyph=$null; if(-not $face.TryGetGlyphTypeface([ref]$glyph)){throw ('Cannot resolve: '+$name)}; if($glyph.CharacterToGlyphMap.Count -lt 90){throw ('Incomplete font: '+$name)}; Write-Output $name }";
        var psi = OrchDesk.Core.Tools.ProcessRunner.Exe("powershell.exe", ["-NoProfile", "-NonInteractive", "-EncodedCommand", Convert.ToBase64String(System.Text.Encoding.Unicode.GetBytes(script))], Root);
        var result = await OrchDesk.Core.Tools.ProcessRunner.RunAsync(psi, TimeSpan.FromSeconds(30), default);
        Assert.True(result.ExitCode == 0, result.Output);
        foreach (var name in new[] { "Inter", "Zodiak", "Geist Mono" }) Assert.Contains(name, result.Output);
    }
    [Fact] public void AllTokenReferencesResolve()
    {
        XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";
        var app = Path.Combine(Root, "src", "OrchDesk.App");
        var files = Directory.GetFiles(app, "*.xaml", SearchOption.AllDirectories).Where(f => !f.Contains(Path.DirectorySeparatorChar + "obj" + Path.DirectorySeparatorChar)).ToArray();
        var keys = files.SelectMany(f => XDocument.Load(f).Descendants().Select(e => (string?)e.Attribute(x + "Key"))).Where(k => k is not null).ToHashSet();
        foreach (var file in files) foreach (Match match in Regex.Matches(File.ReadAllText(file), @"\{(?:StaticResource|DynamicResource) ([\w.]+)\}")) Assert.True(keys.Contains(match.Groups[1].Value), file + ": " + match.Value);
    }
    [Fact] public void PaletteSearchMatchesAllWordsAndArguments()
    {
        PaletteCommand[] commands = [new("model", "Claude Sonnet", "Models", ["cloud"]), new("overnight", "Set up run", "Overnight", ["goal", "until"]), new("files", "Browse files", "Files", [])];
        Assert.Equal("model", Assert.Single(PaletteCommand.Search(commands, "CLOUD son")).Id);
        Assert.Equal("overnight", Assert.Single(PaletteCommand.Search(commands, "goal until")).Id);
        Assert.Empty(PaletteCommand.Search(commands, "cloud missing")); Assert.Equal(3, PaletteCommand.Search(commands, "").Count);
    }
    [Fact] public void TreeGroupsSortsDeduplicatesAndMarksFiles()
    {
        var tree = FileNode.Build(["src/a.cs", "src\\a.cs", "src/b.cs", "readme.md", "../escape.txt", "CRM/private.cs"], new Dictionary<string, string> { ["src/a.cs"] = "Modified" });
        Assert.Equal(2, tree.Count); Assert.Equal("src", tree[0].Name); Assert.Equal(2, tree[0].Children.Count); Assert.Equal("Modified", tree[0].Children[0].Change); Assert.Empty(tree[1].Children);
    }
    [Fact] public void WorkspaceChangeBadgesExcludePrivateAndUnsafePaths()
    {
        var changes = WorkspaceInspection.ParseChanges("M\tsrc/a.cs\nA\tsrc/b.cs\nD\told.txt\nM\t../outside\nM\tCRM/a.cs\n", "new.txt\n.github/workflows/job.yml\nwarning: ignore file\n");
        Assert.Equal(4, changes.Count); Assert.Equal("Modified", changes["src/a.cs"]); Assert.Equal("Added", changes["new.txt"]); Assert.Equal("Deleted", changes["old.txt"]);
    }
    [Fact] public void UsageReadsHistoryAndOneCheckpointPerRunAndIgnoresInvalidFiles()
    {
        var folder = Path.Combine(Path.GetTempPath(), "orchdesk-usage-" + Guid.NewGuid().ToString("N"));
        try {
            Directory.CreateDirectory(Path.Combine(folder, "history")); Directory.CreateDirectory(Path.Combine(folder, "runs", "run1"));
            File.WriteAllText(Path.Combine(folder, "history", "chat.json"), "{\"ProviderId\":\"local\",\"Messages\":[{},{}]}");
            File.WriteAllText(Path.Combine(folder, "history", "bad.json"), "broken");
            File.WriteAllText(Path.Combine(folder, "runs", "run1", "checkpoint.json"), "{\"ManagerId\":\"local\",\"Steps\":7,\"Messages\":[{}]}");
            File.WriteAllText(Path.Combine(folder, "runs", "run1", "other.json"), "{\"ManagerId\":\"local\",\"Steps\":99}");
            Assert.Equal(new ProviderUsage("local", 3, 1, 7), Assert.Single(UsageAggregator.Read(folder)));
        } finally { Directory.Delete(folder, true); }
    }
}
