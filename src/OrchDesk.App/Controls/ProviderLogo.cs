using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using OrchDesk.Core.Config;
using OrchDesk.App.ViewModels;
namespace OrchDesk.App.Controls;

public sealed class ProviderLogo : ContentControl
{
    public static readonly DependencyProperty ProviderProperty = DependencyProperty.Register(nameof(Provider), typeof(object), typeof(ProviderLogo), new PropertyMetadata(null, Changed));
    public object? Provider { get => GetValue(ProviderProperty); set => SetValue(ProviderProperty, value); }
    public ProviderLogo()
    {
        Width = 16; Height = 16;
        HorizontalContentAlignment = HorizontalAlignment.Stretch;
        VerticalContentAlignment = VerticalAlignment.Stretch;
        SetResourceReference(ForegroundProperty, "TextBrush");
    }
    private static void Changed(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var logo = (ProviderLogo)d;
        if (e.OldValue is INotifyPropertyChanged old) PropertyChangedEventManager.RemoveHandler(old, logo.ProviderChanged, "");
        if (e.NewValue is INotifyPropertyChanged current) PropertyChangedEventManager.AddHandler(current, logo.ProviderChanged, "");
        logo.Refresh();
    }
    private void ProviderChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is "Id" or "Model" or "Kind") Refresh();
    }
    private void Refresh()
    {
        var (id, model, kind) = Provider switch
        {
            ProviderEdit p => (p.Id, p.Model, p.Kind),
            ProviderConfig p => (p.Id, p.Model, p.Kind),
            ModelPickerOption p => (p.Id ?? "", p.Model ?? p.DisplayModel, p.Kind),
            _ => (Provider?.ToString() ?? "", (string?)null, ProviderKind.OpenAiCompatible)
        };
        var text = (string.IsNullOrWhiteSpace(model) ? id : model).ToLowerInvariant();
        var key = kind == ProviderKind.ClaudeCli || text.Contains("claude") ? "claude-color"
            : kind == ProviderKind.OpenCodeCli || text.Contains("opencode") ? "opencode"
            : kind == ProviderKind.CodexCli || text.Contains("codex") ? "codex-color"
            : text.Contains("ollama") ? "ollama" : text.Contains("gemma") ? "gemma-color"
            : text.Contains("bonsai") || text.Contains("nemotron") ? "lmstudio"
            : text.Contains("qwen") || text.Contains("local-pc") ? "qwen-color"
            : text.Contains("remote-pc") || text.Contains("lmstudio") || text.Contains("lm studio") ? "lmstudio"
            : text.Contains("openai") ? "openai" : text.Contains("anthropic") ? "anthropic" : null;
        if (Provider is ModelPickerOption { Id: null })
        {
            Content = new Viewbox { Stretch = System.Windows.Media.Stretch.Uniform, Child = new Image
            {
                Source = new System.Windows.Media.Imaging.BitmapImage(new Uri("pack://application:,,,/OrchDesk;component/Assets/orch-app-icon-512.png")),
                Stretch = System.Windows.Media.Stretch.Uniform,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            } };
            return;
        }
        FrameworkElement content = key is null ? new TextBlock { Text = id.Length > 0 ? id[..1].ToUpperInvariant() : "A", HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center }
            : new Image { Source = (System.Windows.Media.ImageSource)FindResource("Logo." + key), Stretch = System.Windows.Media.Stretch.Uniform,
                HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
                RenderTransformOrigin = new Point(0.5, 0.5),
                RenderTransform = new System.Windows.Media.ScaleTransform(key == "opencode" ? 0.8 : 1, key == "opencode" ? 0.8 : 1) };
        Content = new Viewbox { Stretch = System.Windows.Media.Stretch.Uniform, Child = content, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
    }
}
