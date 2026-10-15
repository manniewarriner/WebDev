using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using OrchDesk.App.Services;
using OrchDesk.App.ViewModels;
using OrchDesk.Core.Presentation;

namespace OrchDesk.App;

public partial class MainWindow
{
    private Grid? _modal;
    private FrameworkElement? _returnFocus;
    private Grid? _dismissingModal;
    private readonly List<(PaletteCommand Command, Action Run)> _commands = [];
    private Brush Brush(string key) => (Brush)FindResource(key + "Brush");
    private TextBlock Label(string text, string tone = "Text", double size = 14) { TextBlock label = new TextBlock(); label.Text = text; label.SetResourceReference(TextBlock.ForegroundProperty, tone + "Brush"); label.SetResourceReference(TextBlock.FontSizeProperty, size == 11 ? "Type.Label" : size == 13 ? "Type.Small" : size == 15 ? "Type.Large" : size == 24 ? "Type.H2" : "Type.Body"); label.TextWrapping = TextWrapping.Wrap; label.LineHeight = size * 1.5; return label; }
    private TextBlock HeadingText(string text) { var t = Label(text, size: 24); t.FontFamily = (FontFamily)FindResource("UiFont"); t.Margin = new Thickness(0, 0, 0, 16); return t; }
    private Button ActionButton(string text, Action action, bool primary = false) { var b = new Button { Content = text, Margin = (Thickness)FindResource("Inset.0_8_8_0"), MinHeight = 36 }; if (primary) b.Style = (Style)FindResource("AccentButton"); b.Click += (_, _) => action(); return b; }
    private Border Card(UIElement content) => new() { Child = content, Style = (Style)FindResource("Card"), Margin = new Thickness(0, 0, 0, 12) };
    private void InitializeRedesign()
    {
        SizeChanged += (_, _) => UpdateToolbar();
        Loaded += (_, _) => UpdateToolbar();
        WorkModeSegment.SelectionChanged += index => { if (index == 1) ShowOvernight(); };
        ThemeSegment.SelectionChanged += index => { if (DataContext is MainViewModel vm && vm.IsDark != (index == 0)) vm.ToggleThemeCommand.Execute(null); };
        DataContextChanged += (_, _) => { if (DataContext is not MainViewModel vm) return; ThemeSegment.SelectedIndex = vm.IsDark ? 0 : 1; vm.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(MainViewModel.IsDark)) ThemeSegment.SelectedIndex = vm.IsDark ? 0 : 1; }; var wasBusy = false; vm.Chat.PropertyChanged += (_, e) => { if (e.PropertyName != nameof(ChatViewModel.IsBusy)) return; if (wasBusy && !vm.Chat.IsBusy) ShowToast(vm.Chat.Items.LastOrDefault() is ErrorItem ? "Job needs attention. Open the thread to review." : "Job finished. Your results are ready.", vm.Chat.Items.LastOrDefault() is ErrorItem); wasBusy = vm.Chat.IsBusy; }; };
        PreviewKeyDown += (_, e) => {
            if (e.Key == Key.K && Keyboard.Modifiers == ModifierKeys.Control) { ShowPalette(); e.Handled = true; }
            else if (e.Key == Key.Escape && _modal is not null) { DismissModal(); e.Handled = true; }
        };
        Loaded += (_, _) => {
            if (WelcomeOverlay.Child is StackPanel content) {
                WelcomeOverlay.Child = null;
                var layout = new Grid(); layout.Children.Add(new Controls.AuroraBackground()); layout.Children.Add(content); WelcomeOverlay.Child = layout;
            }
        };
    }
    private void UpdateToolbar()
    {
        var compact = ActualWidth - Sidebar.ActualWidth < 900;
        CommandsAction.Visibility = UsageAction.Visibility = FilesAction.Visibility = compact ? Visibility.Collapsed : Visibility.Visible;
        MoreActions.Visibility = Visibility.Visible;
        ApplyLayout();
        ModelPickerOverlay.Width = Math.Min(600, Math.Max(320, ActualWidth - Sidebar.ActualWidth - 72));
    }
    private void OpenMoreActions(object sender, RoutedEventArgs e)
    {
        var menu = new ContextMenu();
        menu.SetResourceReference(Control.BackgroundProperty, "RaisedBrush");
        menu.SetResourceReference(Control.ForegroundProperty, "TextBrush");
        menu.SetResourceReference(Control.BorderBrushProperty, "BorderBrush");
        void Add(string title, Action action) { var item = new MenuItem { Header = title }; item.SetResourceReference(Control.ForegroundProperty, "TextBrush"); item.Click += (_, _) => action(); menu.Items.Add(item); }
        Add("Delete chat", () => { if (DataContext is MainViewModel vm && vm.Chat.Items.FirstOrDefault() is { } first && _history.FirstOrDefault(h => h.Items.FirstOrDefault() == first) is { } chat) ConfirmDeleteChat(chat); });
        menu.Items.Add(new Separator());
        Add("Filter tools", ShowToolFilters); Add("Usage", ShowUsage); Add("Files", ShowFiles); Add("Commands  Ctrl+K", ShowPalette);
        MoreActions.ContextMenu = menu; menu.PlacementTarget = MoreActions; menu.IsOpen = true;
    }
    private StackPanel ShowModal(string title, double width = 620, bool animate = true)
    {
        if (_dismissingModal is not null) { ShellRoot.Children.Remove(_dismissingModal); _dismissingModal = null; }
        CloseModal(); _returnFocus = Keyboard.FocusedElement as FrameworkElement;
        var snapshot = new System.Windows.Media.Imaging.RenderTargetBitmap(Math.Max(1, (int)ShellRoot.ActualWidth), Math.Max(1, (int)ShellRoot.ActualHeight), 96, 96, PixelFormats.Pbgra32);
        snapshot.Render(ShellRoot); snapshot.Freeze();
        _modal = new Grid { Background = Brush("Overlay") }; Grid.SetColumnSpan(_modal, 2); Panel.SetZIndex(_modal, 200);
        _modal.Children.Add(new Image { Source = snapshot, Stretch = Stretch.Fill, IsHitTestVisible = false, Effect = (Effect)FindResource("Blur.Overlay") });
        _modal.Children.Add(new Border { Background = Brush("Overlay"), IsHitTestVisible = false });
        _modal.MouseDown += (_, e) => { if (e.OriginalSource == _modal) DismissModal(); };
        var stack = new StackPanel();
        var heading = new DockPanel(); var close = ActionButton("×", DismissModal); close.ToolTip = "Close · Esc"; close.Style = (Style)FindResource("GhostButton"); close.Margin = new Thickness(8, 0, 0, 0); close.Height = 32; close.MinHeight = 32; close.Padding = new Thickness(8, 0, 8, 0); DockPanel.SetDock(close, Dock.Right); heading.Children.Add(close); heading.Children.Add(HeadingText(title)); stack.Children.Add(heading);
        var panel = new Border { Child = new ScrollViewer { Content = stack, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, MaxHeight = Math.Max(320, ActualHeight - 160) }, Width = Math.Min(width, Math.Max(320, ActualWidth - 48)), Padding = new Thickness(24), CornerRadius = (CornerRadius)FindResource("Radius.Dialog"), Background = Brush("Surface"), BorderBrush = Brush("Border"), BorderThickness = new Thickness(1), VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center };
        panel.Effect = (Effect)FindResource("Shadow.Card"); _modal.Children.Add(panel); ShellRoot.Children.Add(_modal); KeyboardNavigation.SetTabNavigation(_modal, KeyboardNavigationMode.Cycle); if (animate) Motion.Morph(panel, _returnFocus); return stack;
    }
    private void DismissModal()
    {
        if (_modal is null) return;
        var modal = _modal; var origin = _returnFocus; _modal = null; _dismissingModal = modal; modal.IsHitTestVisible = false;
        void Done() { ShellRoot.Children.Remove(modal); if (_dismissingModal == modal) _dismissingModal = null; if (_modal is null) { WorkModeSegment.SelectedIndex = 0; origin?.Focus(); } }
        if (modal.Children.OfType<Border>().LastOrDefault() is { } panel) Motion.MorphOut(panel, origin, Done); else Done();
    }
    private void CloseModal()
    {
        if (_modal is null) return;
        ShellRoot.Children.Remove(_modal); _modal = null;
        WorkModeSegment.SelectedIndex = 0;
        _returnFocus?.Focus();
    }
    private void OpenPaletteClick(object sender, RoutedEventArgs e) => ShowPalette();
    private void OpenUsageClick(object sender, RoutedEventArgs e) => ShowUsage();
    private void OpenFilesClick(object sender, RoutedEventArgs e) => ShowFiles();
    private void OpenIntegrationsV2(object sender, RoutedEventArgs e) => ShowConnect();
    private void OpenOvernightSetup(object sender, RoutedEventArgs e) => ShowOvernight();
    private void ShowPalette()
    {
        if (DataContext is not MainViewModel vm) return;
        _commands.Clear();
        void Add(string id, string title, string category, Action run, params string[] args) => _commands.Add((new(id, title, category, args), run));
        Add("settings", "Open settings", "Settings", OpenSettingsTab);
        Add("integrations", "Connect a provider", "Settings", ShowConnect, "provider");
        Add("usage", "Usage & run limits", "Accounts", ShowUsage);
        Add("files", "Browse project files", "Files", ShowFiles, "folder");
        Add("folder", "Choose a project folder", "Files", () => vm.Chat.PickFolderCommand.Execute(null));
        Add("overnight", "Set up an overnight run", "Overnight", ShowOvernight, "goal", "until");
        Add("theme", "Switch appearance", "Settings", () => vm.ToggleThemeCommand.Execute(null), "dark / light");
        foreach (var option in vm.Chat.ProviderOptions) { var model = option; Add("model:" + model.Label, model.Label, "Models", () => vm.Chat.SelectedProvider = model, "model"); }
        var body = ShowModal("Where would you like to go?");
        var search = new TextBox { Margin = new Thickness(0, 0, 0, 16), ToolTip = "Search models, files, overnight runs, settings", MinHeight = 48 }; AutomationPropertiesSet(search, "Search commands"); search.SetResourceReference(TextBox.CaretBrushProperty, "AccentBrush"); search.ToolTip = "Search commands or models"; var searchHost = new Grid(); searchHost.Children.Add(search); var placeholder = Label("Search models, files, runs, settings...", "Muted", 14); placeholder.IsHitTestVisible = false; placeholder.VerticalAlignment = VerticalAlignment.Center; placeholder.Margin = new Thickness(16, 0, 8, 0); searchHost.Children.Add(placeholder); body.Children.Add(searchHost); search.TextChanged += (_, _) => placeholder.Visibility = search.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        var list = new ListBox { Margin = new Thickness(0, 0, 0, 16), BorderThickness = new Thickness(0), Background = Brushes.Transparent, Foreground = Brush("Text"), MaxHeight = 315, ItemContainerStyle = (Style)FindResource("ModelPickerItem"), HorizontalContentAlignment = HorizontalAlignment.Stretch };
        body.Children.Add(list);
        void Refresh() { list.Items.Clear(); foreach (var command in PaletteCommand.Search(_commands.Select(c => c.Command), search.Text)) { var row = new DockPanel { Margin = new Thickness(0, 8, 0, 8), Tag = command.Id }; var chip = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(16, 0, 0, 0) }; chip.Children.Add(Label(command.Category, "Muted", 11)); foreach (var argument in command.Arguments) chip.Children.Add(new Border { Background = Brush("Raised"), CornerRadius = (CornerRadius)FindResource("Radius.Pill"), Padding = new Thickness(8, 2, 8, 2), Margin = new Thickness(8, 0, 0, 0), Child = Label(argument, "Muted", 11) }); DockPanel.SetDock(chip, Dock.Right); row.Children.Add(chip); row.Children.Add(Label(command.Title)); list.Items.Add(row); } list.SelectedIndex = list.Items.Count > 0 ? 0 : -1; }
        void Run() { if (list.SelectedItem is not DockPanel { Tag: string id }) return; var command = _commands.First(c => c.Command.Id == id); CloseModal(); command.Run(); }
        search.TextChanged += (_, _) => Refresh();
        search.PreviewKeyDown += (_, e) => { if (e.Key == Key.Down || e.Key == Key.Up) { list.SelectedIndex = Math.Clamp(list.SelectedIndex + (e.Key == Key.Down ? 1 : -1), 0, Math.Max(0, list.Items.Count - 1)); list.ScrollIntoView(list.SelectedItem); e.Handled = true; } else if (e.Key == Key.Enter) { Run(); e.Handled = true; } };
        list.MouseDoubleClick += (_, _) => Run(); list.KeyDown += (_, e) => { if (e.Key == Key.Enter) Run(); };
        body.Children.Add(Label("↑ ↓  Navigate     ↵  Open     Esc  Close", "Muted", 11)); Refresh(); search.Focus();
    }
    private static void AutomationPropertiesSet(DependencyObject target, string name) => System.Windows.Automation.AutomationProperties.SetName(target, name);
    private void ShowConnect()
    {
        var body = ShowModal("Connect your models", 520);
        body.Children.Add(Label("Bring your own accounts. Your providers stay under your control.", "Muted"));
        foreach (var name in new[] { "Claude subscription", "OpenCode", "OpenAI-compatible API", "Local model / LM Studio" }) body.Children.Add(ActionButton(name + "   ↗", () => { CloseModal(); OpenSettingsTab(); }));
        body.Children.Add(Label("Configure the endpoint or sign in from Connection details. Keys are stored with Windows protection.", "Muted", 13));
    }
    private void ShowOvernight()
    {
        if (DataContext is not MainViewModel vm) return;
        var body = ShowModal("Make progress while you rest", 560);
        body.Children.Add(Label("OVERNIGHT RUN", "Accent", 11));
        body.Children.Add(Label("Describe a clear goal. OrchDesk checkpoints progress and queues approvals for your morning review.", "Muted"));
        var goal = new TextBox { AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, MinHeight = 100, Margin = new Thickness(0, 16, 0, 8), Text = vm.Services.UseDemo ? "Polish the workspace and verify the test suite." : "" }; AutomationPropertiesSet(goal, "Overnight goal"); body.Children.Add(goal);
        body.Children.Add(Label($"Up to {vm.Services.Config.Supervisor.MaxRounds} rounds · {vm.Services.Config.MaxSteps} steps per round", "Muted"));
        var shutdown = new CheckBox { Content = "Shutdown PC when done", IsChecked = vm.Chat.ShutdownWhenDone, Margin = new Thickness(0, 16, 0, 8) }; body.Children.Add(shutdown);
        body.Children.Add(ActionButton("Review in composer →", () => { if (string.IsNullOrWhiteSpace(goal.Text)) { goal.Focus(); return; } vm.Chat.ShutdownWhenDone = shutdown.IsChecked == true; vm.Chat.Input = "/overnight " + goal.Text.Trim(); CloseModal(); Composer.Focus(); }, true));
        body.Children.Add(ActionButton("Run settings", () => { CloseModal(); var window = new Views.SettingsWindow(new SettingsViewModel(vm.Services)) { Owner = this }; window.SelectPage("settings-overnight"); window.ShowDialog(); }));
    }
    private void ShowUsage()
    {
        if (DataContext is not MainViewModel vm) return;
        var body = ShowModal("A clear view of your work", 660);
        body.Children.Add(Label("LOCAL ACTIVITY & ACCOUNT HEALTH", "Muted", 11));
        var usage = vm.Services.UseDemo ? new[] { new ProviderUsage("claude-gmail", 84, 6, 122), new ProviderUsage("local-pc", 32, 3, 48) } : UsageAggregator.Read(vm.Services.Store.Directory);
        var totals = new UniformGrid { Columns = 3, Margin = new Thickness(0, 16, 0, 12) };
        foreach (var (caption, value) in new[] { ("Messages", usage.Sum(u => u.Messages)), ("Runs", usage.Sum(u => u.Runs)), ("Tool steps", usage.Sum(u => u.Steps)) }) { var metric = new StackPanel(); metric.Children.Add(Label(caption, "Muted", 13)); metric.Children.Add(HeadingText(value.ToString())); var metricCard = Card(metric); metricCard.Margin = new Thickness(0, 0, 8, 12); metricCard.Padding = new Thickness(16); metricCard.MinHeight = 96; totals.Children.Add(metricCard); } body.Children.Add(totals);
        body.Children.Add(ActionButton("Usage by model", () => ShowModelUsage(vm.ModelUsageRows)));
        var budget = vm.Services.UseDemo ? new RunBudget(26, 40, 3, 4, "Completed") : RunBudget.ReadLatest(vm.Services.Store.Directory);
        if (budget is not null) {
            var panel = new StackPanel(); panel.Children.Add(Label("LATEST RUN  /  " + budget.Status, "Muted", 11));
            foreach (var metric in new[] { ("Tool steps", budget.Steps, budget.MaxSteps), ("Rounds", budget.Round, budget.MaxRounds) }) {
                panel.Children.Add(Label($"{metric.Item1}   {metric.Item2} / {metric.Item3}", "Text", 13));
                var bar = new ProgressBar { Minimum = 0, Maximum = Math.Max(1, metric.Item3), Value = metric.Item2, Height = 5, Margin = new Thickness(0, 8, 0, 16), Background = Brush("Raised"), Foreground = Brush(metric.Item2 >= metric.Item3 ? "Warn" : "Accent"), BorderThickness = new Thickness(0) }; panel.Children.Add(bar);
            } body.Children.Add(Card(panel));
        }
        foreach (var account in vm.Services.Rotator.Accounts) {
            var row = new StackPanel(); row.Children.Add(Label(account.Provider.DisplayName));
            var observed = usage.FirstOrDefault(u => u.ProviderId.Equals(account.Provider.Id, StringComparison.OrdinalIgnoreCase));
            row.Children.Add(Label($"{observed?.Messages ?? 0} messages · {observed?.Runs ?? 0} runs · {account.Health}", "Muted", 13));
            row.Children.Add(Label(account.LastLimitHit is { } hit ? "Cooldown until " + (hit + vm.Services.Rotator.LimitCooldown).ToLocalTime().ToString("HH:mm") : "No active usage limit recorded", account.LastLimitHit is null ? "Muted" : "Warn", 13)); body.Children.Add(Card(row));
        }
        body.Children.Add(Label($"Run budget: {vm.Services.Config.MaxSteps} tool steps · {vm.Services.Config.Supervisor.MaxRounds} rounds. Subscription quotas are not exposed by providers; these are observed local counts.", "Muted", 13));
    }
    private async void ShowFiles()
    {
        if (DataContext is not MainViewModel vm) return;
        var body = ShowModal("Project files", 660);
        body.Children.Add(Label(vm.Chat.Workspace ?? "Choose a folder to explore your project.", "Muted", 13));
        if (vm.Chat.Workspace is null) body.Children.Add(ActionButton("Choose a folder", () => { CloseModal(); vm.Chat.PickFolderCommand.Execute(null); ShowFiles(); }));
        var nodes = vm.Services.UseDemo ? FileNode.Build(["src/OrchDesk.App/MainWindow.xaml", "src/OrchDesk.App/Themes/Dark.xaml", "src/OrchDesk.Core/Presentation/WorkspacePresentation.cs", "tests/OrchDesk.Tests/DesignSystemTests.cs"], new Dictionary<string, string> { ["src/OrchDesk.App/MainWindow.xaml"] = "Modified", ["src/OrchDesk.Core/Presentation/WorkspacePresentation.cs"] = "Added" }) : FileNode.Read(vm.Chat.Workspace ?? "");
        var tree = new TreeView { Background = Brushes.Transparent, BorderThickness = new Thickness(0), Foreground = Brush("Text"), Margin = new Thickness(0, 16, 0, 0) };
        TreeViewItem Item(FileNode node) { var item = new TreeViewItem { Header = node.Name + (node.Change is null ? "" : "    " + node.Change), IsExpanded = true, Foreground = Brush(node.Change == "Added" ? "Success" : node.Change == "Modified" ? "Warn" : "Text"), Padding = new Thickness(4, 6, 4, 6), Tag = node }; foreach (var child in node.Children) item.Items.Add(Item(child)); return item; }
        tree.MouseDoubleClick += (_, _) => {
            if (tree.SelectedItem is not TreeViewItem { Tag: FileNode node } || node.Children.Count > 0 || vm.Chat.Workspace is null) return;
            try {
                var path = OrchDesk.Core.Tools.WorkspacePath.Resolve(vm.Chat.Workspace, node.RelativePath);
                vm.Chat.EditorText = System.IO.File.ReadAllText(path); vm.Chat.EditorPath = path; CloseModal();
            } catch (Exception ex) when (ex is System.IO.IOException or UnauthorizedAccessException) { ShowToast("Unable to open this file", true); }
        };
        foreach (var node in nodes) tree.Items.Add(Item(node)); body.Children.Add(tree);
        if (!vm.Services.UseDemo && vm.Chat.Workspace is { } workspace) {
            try { var inspection = await WorkspaceInspection.ReadAsync(workspace); var actual = nodes.SelectMany(Flatten).Select(n => n.RelativePath).Concat(inspection.Changes.Keys); nodes = FileNode.Build(actual, inspection.Changes); tree.Items.Clear(); foreach (var node in nodes) tree.Items.Add(Item(node)); }
            catch (Exception ex) when (ex is System.IO.IOException or UnauthorizedAccessException or InvalidOperationException) { body.Children.Add(Label("Unable to read all project changes.", "Muted")); }
        }
        body.Children.Add(ActionButton("View job output", () => ShowCode(false))); body.Children.Add(ActionButton("Review changes", () => ShowCode(true)));
    }
    private static IEnumerable<FileNode> Flatten(FileNode node) { yield return node; foreach (var child in node.Children.SelectMany(Flatten)) yield return child; }
    private void CopyToolOutput(object sender, RoutedEventArgs e) { if (sender is FrameworkElement { DataContext: ToolItem tool } && tool.Output is { } output) { Clipboard.SetText(output); ShowToast("Result copied", false); } }
    private async void ShowCode(bool diff)
    {
        if (DataContext is not MainViewModel vm) return;
        var body = ShowModal(diff ? "Changes to review" : "Tool output", 720, false);
        var tools = vm.Chat.Items.OfType<ToolItem>().ToArray();
        var text = vm.Services.UseDemo ? (diff ? "--- Themes/Dark.xaml\n+++ Themes/Dark.xaml\n@@ -1,3 +1,3 @@\n- BaseColor: #1E1612\n+ BaseColor: #161210\n+ AccentColor: #D9734A\n\n  2 additions · 1 deletion" : "$ dotnet test\n\nStarting test execution, please wait…\nPassed!  Failed: 0\n\nBuild succeeded.\n    0 Warning(s)\n    0 Error(s)") : string.Join("\n\n", tools.Select(t => t.Title + "\n" + t.Output));
        if (diff && !vm.Services.UseDemo && vm.Chat.Workspace is { } workspace) { try { text = (await WorkspaceInspection.ReadAsync(workspace)).Diff; } catch (Exception ex) when (ex is System.IO.IOException or UnauthorizedAccessException or InvalidOperationException) { text = "Unable to read this folder's diff."; } }
        if (text.Length == 0) text = "No job output yet. Run a task to inspect its results here.";
        var output = new TextBox { Text = text, Style = (Style)FindResource("CodeText"), Foreground = Brush("Text"), MinHeight = 220, FontSize = 13 };
        body.Children.Add(Label(diff ? "UNIFIED DIFF" : "RESULT · PLAIN TEXT", "Muted", 11)); if (diff) {
            body.Children.Add(Card(new OrchDesk.App.Controls.DiffViewer { Diff = text, MinHeight = 220 }));
        } else body.Children.Add(Card(output));
        body.Children.Add(ActionButton("Copy", () => { Clipboard.SetText(text); ShowToast("Copied to clipboard", false); }));
    }
    private void ShowSkeleton()
    {
        var body = ShowModal("Syncing your models", 560); body.Children.Add(Label("Discovering the models available to your accounts…", "Muted"));
        for (var i = 0; i < 4; i++) { var bar = new Border { Height = 40, Margin = new Thickness(0, 12, 0, 0), CornerRadius = new CornerRadius(6), Background = Brush("Raised"), Opacity = .65 }; body.Children.Add(bar); if (Motion.Enabled) bar.BeginAnimation(OpacityProperty, new DoubleAnimation(.35, .85, TimeSpan.FromSeconds(1.2)) { AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever }); }
    }
    private void ShowToast(string message, bool error)
    {
        var stack = ShellRoot.Children.OfType<StackPanel>().FirstOrDefault(p => Equals(p.Tag, "toasts"));
        if (stack is null) { stack = new StackPanel { Tag = "toasts", HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(24), MaxWidth = 360 }; Grid.SetColumnSpan(stack, 2); Panel.SetZIndex(stack, 300); ShellRoot.Children.Add(stack); }
        var text = Label((error ? "!  " : "✓  ") + message, error ? "Danger" : "Text"); var toast = Card(text); stack.Children.Insert(0, toast); while (stack.Children.Count > 3) stack.Children.RemoveAt(3); Motion.Reveal(toast);
        if (!Motion.Screenshot) { var timer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(5) }; timer.Tick += (_, _) => { stack.Children.Remove(toast); timer.Stop(); }; timer.Start(); }
    }
    public void PrepareRedesignScreenshot(string? page)
    {
        if (DataContext is not MainViewModel vm) return;
        if (page is "delete-confirm" or "delete-undo") {
            var sample = new ChatSnapshot("Review the workspace layout", [new UserItem("Review the workspace layout")], null);
            vm.Chat.Restore(sample.Items, null); _history.Add(sample);
            if (page == "delete-confirm") ConfirmDeleteChat(sample); else DeleteChat(sample);
        }
        if (page == "home" && vm.Services.UseDemo) vm.Chat.Workspace = null;
        switch (page) {
            case "accounts": vm.Page = "Accounts"; break;
            case "palette": ShowPalette(); break; case "connect-dialog": ShowConnect(); break;
            case "overnight-setup": ShowOvernight(); break; case "usage": ShowUsage(); break; case "usage-by-model": foreach (var model in vm.Services.AutoModels().Take(4)) { vm.Services.Usage.Start(model); vm.Services.Usage.Complete(model, new(240, 80)); } ShowModelUsage(vm.ModelUsageRows); break;
            case "file-tree": ShowFiles(); break; case "diff": ShowCode(true); break;
            case "code-block": ShowCode(false); break; case "skeleton": ShowSkeleton(); break;
            case "toasts": ShowToast("Overnight run complete. Ready for review.", false); ShowToast("Provider unavailable. Fallback selected.", true); ShowToast("Settings saved", false); break;
        }
    }
}
