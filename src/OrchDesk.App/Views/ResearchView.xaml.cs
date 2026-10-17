using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Microsoft.Web.WebView2.Core;
using OrchDesk.Core.Presentation;
using OrchDesk.Core.Research;
using OrchDesk.Core.Models;
using OrchDesk.Core.Automation;
using OrchDesk.App.ViewModels;

namespace OrchDesk.App.Views;

public sealed record NotebookItem(string Title, string Href);

// Research page: Gemini answers, optionally grounded in a NotebookLM notebook that the user opened in the embedded browser.
public partial class ResearchView : UserControl
{
    private const string Home = "https://notebooklm.google.com/";
    private static readonly string[] GoogleHosts = ["google.com", "googleusercontent.com", "gstatic.com", "googleapis.com"];
    private readonly ObservableCollection<ResearchMessage> items = [];
    private readonly ObservableCollection<NotebookItem> notebooks = [];
    private readonly List<ChatMessage> history = [];
    private string? sourceTitle, sourceText;
    private bool webReady;
    private CancellationTokenSource? asking;
    private MainViewModel? Vm => DataContext as MainViewModel;

    public ResearchView()
    {
        InitializeComponent();
        Chat.ItemsSource = items; NotebookList.ItemsSource = notebooks;
        items.CollectionChanged += (_, _) => EmptyAsk.Visibility = items.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        // Load when the page is first shown, and again each time so a newly saved key is picked up.
        ShowAsk();
        IsVisibleChanged += async (_, a) => { if (a.NewValue is true && DataContext != null) await LoadGeminiAsync(); };
        Unloaded += (_, _) => asking?.Cancel();
    }

    private void ShowAsk() { AskPane.Visibility = Visibility.Visible; WebPane.Visibility = Visibility.Collapsed; TabAsk.Style = (Style)FindResource("AccentButton"); TabWeb.Style = (Style)FindResource("GhostButton"); }
    private void ShowWeb() { AskPane.Visibility = Visibility.Collapsed; WebPane.Visibility = Visibility.Visible; TabAsk.Style = (Style)FindResource("GhostButton"); TabWeb.Style = (Style)FindResource("AccentButton"); }

    // ---- Gemini ---------------------------------------------------------------------------------------
    private async Task LoadGeminiAsync()
    {
        if (Vm is not { } vm) return;
        var research = vm.Services.Research;
        if (!research.Linked) { GeminiStatus.Text = "Not linked. Create a free key at Google AI Studio, then paste it under Settings, API keys, gemini."; LinkButton.Visibility = Visibility.Visible; ModelBox.ItemsSource = null; return; }
        LinkButton.Visibility = Visibility.Collapsed; GeminiStatus.Text = "Linked. Loading models…";
        try
        {
            var models = await research.ModelsAsync();
            ModelBox.ItemsSource = models; ModelBox.SelectedItem = ResearchClient.Default(models);
            GeminiStatus.Text = models.Count == 0 ? "Linked, but Google returned no chat models." : $"Linked. {models.Count} models available.";
        }
        catch (Exception e) { GeminiStatus.Text = "Linked, but the model list failed: " + e.Message; }
    }
    private async void ReloadClick(object sender, RoutedEventArgs e) => await LoadGeminiAsync();
    private void LinkClick(object sender, RoutedEventArgs e)
    {
        try { Process.Start(new ProcessStartInfo("https://aistudio.google.com/apikey") { UseShellExecute = true }); } catch (Exception) { /* no browser available */ }
        if (Vm is { } vm) { vm.OpenSettingsCommand.Execute(null); }
    }
    private void TabAskClick(object sender, RoutedEventArgs e) => ShowAsk();
    private void QuestionKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && (Keyboard.Modifiers & ModifierKeys.Shift) == 0) { e.Handled = true; AskClick(sender, e); }
    }
    private async void AskClick(object sender, RoutedEventArgs e)
    {
        if (asking != null) { asking.Cancel(); return; }
        var question = Question.Text.Trim(); if (question.Length == 0 || Vm is not { } vm) return;
        if (ModelBox.SelectedItem is not string model) { items.Add(new() { Role = "gemini", Text = vm.Services.Research.Linked ? "No Gemini model is loaded yet. Press Reload." : "Link Gemini first: paste your AI Studio key under Settings, API keys." }); return; }
        Question.Clear(); items.Add(new() { Role = "you", Text = question });
        var answer = new ResearchMessage { Role = "gemini", Text = "…" }; items.Add(answer);
        var cts = asking = new(); AskButton.Content = "Stop";
        try
        {
            var text = new System.Text.StringBuilder();
            await foreach (var piece in vm.Services.Research.AskAsync(model, ResearchClient.Build(question, sourceTitle, sourceText, history), cts.Token)) { text.Append(piece); answer.Text = text.ToString(); AskScroll.ScrollToEnd(); }
            if (text.Length == 0) answer.Text = "Gemini returned no text.";
            else { vm.Services.UsageLog.Record(new(DateTime.UtcNow, "gemini/" + model, "research", OrchDesk.Core.Telemetry.UsageLog.Estimate(question + sourceText), OrchDesk.Core.Telemetry.UsageLog.Estimate(text.ToString()))); history.Add(ChatMessage.User(question)); history.Add(ChatMessage.Assistant(text.ToString())); }
        }
        catch (OperationCanceledException) { if (answer.Text == "…") answer.Text = "Stopped."; }
        catch (UsageLimitException) { answer.Text = "Gemini quota reached for this model (free keys have no quota on some Pro models). Pick a Flash model or try again later."; }
        catch (Exception ex) { answer.Text = ResearchClient.Explain(ex); }
        finally { asking = null; cts.Dispose(); AskButton.Content = "Ask"; }
    }

    // ---- NotebookLM -----------------------------------------------------------------------------------
    private static bool IsGoogle(string? url) => Uri.TryCreate(url, UriKind.Absolute, out var u) && u.Scheme == Uri.UriSchemeHttps && GoogleHosts.Any(h => u.Host == h || u.Host.EndsWith("." + h, StringComparison.OrdinalIgnoreCase));
    private async Task<bool> EnsureWebAsync()
    {
        if (webReady) return true;
        try
        {
            var folder = Path.Combine(Vm!.Services.Store.Directory, "webview");
            Directory.CreateDirectory(folder);
            await Web.EnsureCoreWebView2Async(await CoreWebView2Environment.CreateAsync(null, folder));
        }
        catch (Exception e) { WebStatus.Text = "The embedded browser could not start (" + e.Message + "). Install the Microsoft Edge WebView2 runtime."; return false; }
        var core = Web.CoreWebView2;
        core.Settings.AreDevToolsEnabled = false; core.Settings.IsStatusBarEnabled = false;
        core.NavigationStarting += (_, a) => { if (!IsGoogle(a.Uri)) { a.Cancel = true; OpenExternal(a.Uri); } };
        core.NewWindowRequested += (_, a) => { a.Handled = true; if (IsGoogle(a.Uri)) core.Navigate(a.Uri); else OpenExternal(a.Uri); };
        core.SourceChanged += (_, _) => WebStatus.Text = core.Source.Length > 80 ? core.Source[..80] + "…" : core.Source;
        core.NavigationCompleted += async (_, a) => { if (a.IsSuccess && core.Source.StartsWith(Home, StringComparison.OrdinalIgnoreCase) && !core.Source.Contains("/notebook/")) { await Task.Delay(2500); await ScanAsync(); } };
        webReady = true; return true;
    }
    private static void OpenExternal(string url) { if (Uri.TryCreate(url, UriKind.Absolute, out var u) && u.Scheme == Uri.UriSchemeHttps) try { Process.Start(new ProcessStartInfo(u.ToString()) { UseShellExecute = true }); } catch (Exception) { /* ignored */ } }
    private async void OpenWebClick(object sender, RoutedEventArgs e)
    {
        ShowWeb(); if (!await EnsureWebAsync()) return;
        if (string.IsNullOrEmpty(Web.CoreWebView2.Source) || Web.CoreWebView2.Source == "about:blank") Web.CoreWebView2.Navigate(Home);
    }
    private async void HomeClick(object sender, RoutedEventArgs e) { if (await EnsureWebAsync()) Web.CoreWebView2.Navigate(Home); }
    private async void ScanClick(object sender, RoutedEventArgs e) { if (!await EnsureWebAsync()) return; await ScanAsync(); }
    private async Task ScanAsync()
    {
        if (!webReady) return;
        try
        {
            // NotebookLM is a single-page app with no public API; read the notebook links the signed-in page shows.
            const string script = "JSON.stringify([...document.querySelectorAll('a[href*=\"/notebook/\"]')].map(a=>({Title:((a.innerText||a.getAttribute('aria-label')||'').trim().split('\\n').filter(Boolean)[0]||''),Href:a.href})).filter(x=>x.Title))";
            var raw = await Web.CoreWebView2.ExecuteScriptAsync(script);
            var json = JsonSerializer.Deserialize<string>(raw) ?? "[]";
            var found = JsonSerializer.Deserialize<List<NotebookItem>>(json) ?? [];
            notebooks.Clear(); foreach (var n in found.Where(n => IsGoogle(n.Href)).GroupBy(n => n.Href).Select(g => g.First())) notebooks.Add(n);
            NotebookHint.Text = notebooks.Count > 0 ? $"{notebooks.Count} notebooks found." : "No notebooks found yet. Sign in, wait for the list to load, then refresh. You can always open a notebook by hand in the NotebookLM tab.";
        }
        catch (Exception ex) { NotebookHint.Text = "Could not read the notebook list: " + ex.Message; }
    }
    private async void NotebookPicked(object sender, SelectionChangedEventArgs e)
    {
        if (NotebookList.SelectedItem is not NotebookItem n) return;
        NotebookList.SelectedItem = null; ShowWeb(); if (await EnsureWebAsync()) Web.CoreWebView2.Navigate(n.Href);
    }
    private async void UseContextClick(object sender, RoutedEventArgs e)
    {
        if (!await EnsureWebAsync()) return;
        try
        {
            var text = JsonSerializer.Deserialize<string>(await Web.CoreWebView2.ExecuteScriptAsync("document.body.innerText")) ?? "";
            var title = JsonSerializer.Deserialize<string>(await Web.CoreWebView2.ExecuteScriptAsync("document.title")) ?? "";
            if (text.Trim().Length < 80) { WebStatus.Text = "Open a notebook first, then choose Use this notebook as context."; return; }
            sourceText = text; sourceTitle = title.Replace(" - NotebookLM", "").Trim();
            ContextText.Text = $"Using notebook: {(sourceTitle.Length > 0 ? sourceTitle : "current page")} · {Math.Min(text.Length, ResearchClient.MaxContextChars):N0} characters";
            ContextChip.Visibility = Visibility.Visible; ShowAsk(); Question.Focus();
        }
        catch (Exception ex) { WebStatus.Text = "Could not read the page: " + ex.Message; }
    }
    private void ClearContextClick(object sender, RoutedEventArgs e) { sourceText = sourceTitle = null; ContextChip.Visibility = Visibility.Collapsed; }
}
