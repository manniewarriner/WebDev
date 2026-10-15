using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;

namespace OrchDesk.App.Controls;

/// <summary>The unified diff renderer shared by change review and inline approvals.</summary>
public sealed class DiffViewer : RichTextBox
{
    public static readonly DependencyProperty DiffProperty = DependencyProperty.Register(nameof(Diff), typeof(string), typeof(DiffViewer), new PropertyMetadata("", (d, _) => ((DiffViewer)d).Render()));
    public string Diff { get => (string)GetValue(DiffProperty); set => SetValue(DiffProperty, value); }
    public DiffViewer() { IsReadOnly = true; BorderThickness = new(0); Background = Brushes.Transparent; VerticalScrollBarVisibility = ScrollBarVisibility.Auto; MaxHeight = 260; Loaded += (_, _) => Render(); }
    private void Render()
    {
        var doc = new FlowDocument { FontFamily = TryFindResource("CodeFont") as FontFamily ?? new FontFamily("Consolas"), FontSize = 13, PagePadding = new(0), Background = Brushes.Transparent };
        foreach (var line in (Diff ?? "").Split('\n')) {
            var paragraph = new Paragraph(new Run(line.Length == 0 ? " " : line)) { Margin = new(0), LineHeight = 21 };
            paragraph.SetResourceReference(ForegroundProperty, line.StartsWith('+') ? "SuccessBrush" : line.StartsWith('-') ? "DangerBrush" : line.StartsWith("@@") ? "AccentBrush" : "TextBrush");
            doc.Blocks.Add(paragraph);
        }
        Document = doc;
    }
}
