using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using System.Collections.ObjectModel;
using OrchDesk.App.ViewModels;
using OrchDesk.App.Services;
using OrchDesk.Core.Config;

namespace OrchDesk.App;

public partial class MainWindow : Window
{
    private bool _stickToBottom = true;
    private bool _collapsed;
    private readonly ObservableCollection<ChatSnapshot> _history = [];
    public sealed record ChatSnapshot(string Title, ChatItem[] Items, OrchDesk.Core.Orchestration.Conversation? Conversation) { public DateTimeOffset Date {get;init;} = DateTimeOffset.Now; public string Model {get;init;} = ""; public string? Workspace {get;init;} public string? ProviderId {get;init;} }
    public MainWindow()
    {
        InitializeComponent();
        InitializeFollowup();
        InitializeRedesign();
        ApplyLayout();
        HistoryList.ItemsSource = _history;
        DataContextChanged += (_, _) =>
        {
            if (DataContext is not MainViewModel vm) return;
            Motion.ReduceMotion = vm.Services.Config.ReduceMotion;
            UpdateChatView(false);
            SetSidebar(vm.Services.Config.SidebarCollapsed, false);
            vm.Chat.ContentAdded += FollowContent;
            vm.Chat.PropertyChanged += (_, e) => {
                if (e.PropertyName == nameof(ChatViewModel.ViewState)) UpdateChatView(true);
                if (e.PropertyName == nameof(ChatViewModel.SelectedProvider)) Motion.Reveal(ModelNameLabel);
            };
            vm.Chat.Items.CollectionChanged += (_, e) => {
                if (e.Action == System.Collections.Specialized.NotifyCollectionChangedAction.Reset) { _stickToBottom = true; JumpLatest.Visibility = Visibility.Collapsed; return; }
                Dispatcher.BeginInvoke(() => {
                    var title = vm.Chat.Items.OfType<UserItem>().FirstOrDefault()?.Text;
                    if (title is null || (vm.Chat.Items.FirstOrDefault() is { } firstItem && _deletedChats.Contains(firstItem))) return;
                    var snapshot = new ChatSnapshot(title, vm.Chat.Items.ToArray(), vm.Chat.CurrentConversation) {Model=vm.Chat.SelectedProvider?.ModelName??"",Date=vm.Chat.Items.First().Timestamp,Workspace=vm.Chat.Workspace,ProviderId=vm.Chat.SelectedProvider?.Id};
                    var previous = _history.FirstOrDefault(h => h.Items.FirstOrDefault() == snapshot.Items.FirstOrDefault());
                    if (previous is not null) _history.Remove(previous);
                    _history.Insert(0, snapshot);
                    while (_history.Count > 8) _history.RemoveAt(_history.Count - 1);
                }, DispatcherPriority.Background);
            };
        };
        Loaded += (_, _) => { Composer.Focus(); Motion.Changed += MotionChanged; };
        ModelPickerOverlay.IsVisibleChanged += (_, _) => { if (!ModelPickerOverlay.IsVisible) StopPickerAnimations(); };
        IsVisibleChanged += (_, _) => { if (!IsVisible) Sidebar.BeginAnimation(WidthProperty, null); };
        Closed += (_, _) => { Motion.Changed -= MotionChanged; if (DataContext is MainViewModel vm) vm.Chat.ContentAdded -= FollowContent; };
    }
    private void UpdateChatView(bool animate)
    {
        if (DataContext is not MainViewModel vm) return;
        var home = vm.Chat.ViewState == ChatViewState.Home;
        HomeContent.BeginAnimation(OpacityProperty, null);
        HomeContent.IsHitTestVisible = home;
        ChatScroll.Visibility = home ? Visibility.Collapsed : Visibility.Visible;
        if (home) { HomeContent.Visibility = Visibility.Visible; HomeContent.ScrollToTop(); }
        if (animate && Motion.Enabled)
        {
            var fade = new DoubleAnimation(home ? 0 : 1, home ? 1 : 0, TimeSpan.FromMilliseconds(180));
            fade.Completed += (_, _) => { if (vm.Chat.ViewState == ChatViewState.Thread) HomeContent.Visibility = Visibility.Collapsed; };
            HomeContent.BeginAnimation(OpacityProperty, fade);
        }
        else { HomeContent.Opacity = home ? 1 : 0; HomeContent.Visibility = home ? Visibility.Visible : Visibility.Collapsed; }
    }

    private void StopPickerAnimations()
    {
        Motion.StopReveal(ModelPickerOverlay);
        foreach (var bar in new[] { SpeedBar, CostBar, ContextBar })
            ((ScaleTransform)bar.RenderTransform).BeginAnimation(ScaleTransform.ScaleXProperty, null);
    }
    private void MotionChanged()
    {
        if (Motion.Enabled) return;
        UpdateChatView(false);
        StopPickerAnimations();
        Motion.StopReveal(ModelNameLabel);
        Sidebar.BeginAnimation(WidthProperty, null);
    }
    private void FollowContent() => Dispatcher.BeginInvoke(() => { if (_stickToBottom) ChatScroll.ScrollToEnd(); }, DispatcherPriority.Loaded);
    private void ThreadScrolled(object sender, ScrollChangedEventArgs e)
    {
        if (e.ExtentHeightChange == 0) _stickToBottom = ChatScroll.ScrollableHeight - ChatScroll.VerticalOffset < 32;
        if (_stickToBottom && e.ExtentHeightChange != 0) ChatScroll.ScrollToEnd();
        JumpLatest.Visibility = !_stickToBottom && ChatScroll.ScrollableHeight > 0 ? Visibility.Visible : Visibility.Collapsed;
    }
    private void JumpToLatest(object sender, RoutedEventArgs e) { _stickToBottom = true; ChatScroll.ScrollToEnd(); JumpLatest.Visibility = Visibility.Collapsed; }
    public void OpenModelPicker()
    {
        ModelPickerOverlay.Width = Math.Min(600, Math.Max(340, ActualWidth - Sidebar.Width - 72));
        ModelPickerOverlay.Visibility = Visibility.Visible;
        ModelList.SelectedItem = (DataContext as MainViewModel)?.Chat.SelectedProvider;
        ShowProfile((DataContext as MainViewModel)?.Chat.SelectedProvider);
        Motion.Reveal(ModelPickerOverlay);
        ModelSearch.Focus();
    }
    private void ToggleModelPicker(object sender, RoutedEventArgs e) { if (ModelPickerOverlay.IsVisible) ClosePicker(); else OpenModelPicker(); }
    private void ClosePicker() { ModelPickerOverlay.Visibility = Visibility.Collapsed; ModelPickerButton.Focus(); }
    private void ModelPicked(object sender, SelectionChangedEventArgs e)
    {
        if (ModelList is null) return;
        ShowProfile(ModelList.SelectedItem as ModelPickerOption);
        // Arrow keys preview; Enter or a pointer click confirms.

    }
    private void SearchModels(object sender, TextChangedEventArgs e)
    {
        if (Resources["GroupedModels"] is not CollectionViewSource source || source.View is null) return;
        var query = ModelSearch.Text.Trim();
        var predicate = OrchDesk.Core.Filtering.FilterModel.BuildPredicate<ModelPickerOption>(ModelFilters.Filters, ModelField);
        source.View.Filter = item => item is ModelPickerOption p && (ModelFilters.Filters.Count == 0 || p.Id is not null) && predicate(p) && (p.Label.Contains(query, StringComparison.OrdinalIgnoreCase) || p.ModelName.Contains(query, StringComparison.OrdinalIgnoreCase));
    }
    private void SearchKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape) { ClosePicker(); e.Handled = true; }
        if (e.Key != Key.Down) return;
        if (ModelList.Items.Count > 0) { ModelList.SelectedIndex = 0; ModelList.UpdateLayout(); (ModelList.ItemContainerGenerator.ContainerFromIndex(0) as ListBoxItem)?.Focus(); }
        e.Handled = true;
    }
    private void ConfirmModel() { if (DataContext is MainViewModel vm && ModelList.SelectedItem is ModelPickerOption option) vm.Chat.SelectedProvider = option; ClosePicker(); }
    private void ConfirmPointerModel(object sender, MouseButtonEventArgs e) { if (ItemsControl.ContainerFromElement(ModelList, e.OriginalSource as DependencyObject) is ListBoxItem) ConfirmModel(); }
    private void ModelKeyDown(object sender, KeyEventArgs e) { if (e.Key == Key.Enter) { ConfirmModel(); e.Handled = true; } else if (e.Key == Key.Escape) { ClosePicker(); e.Handled = true; } }
    private void PreviewModel(object sender, MouseEventArgs e)
    {
        var row = ItemsControl.ContainerFromElement(ModelList, e.OriginalSource as DependencyObject) as ListBoxItem;
        if (row?.DataContext is ModelPickerOption option) ShowProfile(option);
    }
    private void FocusModel(object sender, KeyboardFocusChangedEventArgs e)
    {
        var row = ItemsControl.ContainerFromElement(ModelList, e.NewFocus as DependencyObject) as ListBoxItem;
        ShowProfile(row?.DataContext as ModelPickerOption ?? ModelList.SelectedItem as ModelPickerOption);
    }
    private ModelPickerOption? _preview;
    private void ShowProfile(ModelPickerOption? option)
    {
        if (option is null || ProfileName is null || _preview == option) return;
        _preview = option; ProfileName.Text = option.ModelName;
        var profile = ModelProfiles.Get(option.Model ?? option.DisplayModel);
        ContextLabel.Text = "Context · " + profile.ContextLabel;
        foreach (var (bar, value) in new[] { (SpeedBar, profile.Speed), (CostBar, profile.Cost), (ContextBar, profile.Context) }) {
            var scale = (ScaleTransform)bar.RenderTransform;
            var from = scale.ScaleX;
            scale.BeginAnimation(ScaleTransform.ScaleXProperty, null); scale.ScaleX = value;
            if (Motion.Enabled && ModelPickerOverlay.IsVisible) scale.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(from, value, TimeSpan.FromMilliseconds(220)) { FillBehavior = FillBehavior.Stop });
        }
    }
    private void DismissModelPicker(object sender, MouseButtonEventArgs e)
    {
        if (ModelPickerOverlay.IsVisible && !ModelPickerOverlay.IsMouseOver && !ModelPickerButton.IsMouseOver) ModelPickerOverlay.Visibility = Visibility.Collapsed;
    }
    private void ToggleSidebar(object sender, RoutedEventArgs e)
    {
        SetSidebar(!_collapsed, true);
        if (DataContext is MainViewModel vm) { vm.Services.Config.SidebarCollapsed = _collapsed; if (!vm.Services.IsScreenshot) vm.Services.Store.Save(vm.Services.Config); }
    }
    private void SetSidebar(bool collapsed, bool animate)
    {
        _collapsed = collapsed;
        var from = Sidebar.ActualWidth > 0 ? Sidebar.ActualWidth : Sidebar.Width;
        Sidebar.BeginAnimation(WidthProperty, null); Sidebar.Width = collapsed ? 64 : 224;
        RailFooter.Visibility = collapsed ? Visibility.Visible : Visibility.Collapsed;
        foreach (var label in new FrameworkElement[] { AvatarDetails, SidebarTitle, ChatNavLabel, AccountsNavLabel, SidebarFooter, HistoryPanel }) label.Visibility = collapsed ? Visibility.Collapsed : Visibility.Visible;
        if (animate && Motion.Enabled) Sidebar.BeginAnimation(WidthProperty, new DoubleAnimation(from, Sidebar.Width, TimeSpan.FromMilliseconds(230)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseInOut }, FillBehavior = FillBehavior.Stop });
    }
    private void RestoreChat(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: ChatSnapshot snapshot } || DataContext is not MainViewModel vm || vm.Chat.IsBusy) return;
        if(snapshot.Workspace is not null)vm.Chat.Workspace=snapshot.Workspace;
        vm.Chat.Restore(snapshot.Items, snapshot.Conversation);
    }
    public void PrepareScreenshot(string? page)
    {
        if (DataContext is not MainViewModel vm) return;
        var chat = vm.Chat;
        if (page is "team-running" or "team-done") {
            WelcomeOverlay.Visibility = Visibility.Collapsed;
            chat.Items.Clear();
            chat.Items.Add(new UserItem("Implement the workspace activity dashboard and test the integration."));
            var job = new OrchDesk.Core.Orchestration.TeamJob { Pm="Claude Opus", Status=page=="team-done"?"done":"running" };
            var names = new[] { "Plan and assign chunks", "Build dashboard layout", "Connect activity data", "Add integration tests" };
            var models = new[] { "Claude Opus", "GLM 5.2", "Kimi K2.5", "Claude Haiku" };
            for(var i=0;i<4;i++) job.Cards.Add(new() { Id=i.ToString(),Title=names[i],Role=i==0?"PM":"Worker",Model=models[i],Status=page=="team-done"||i==0?"done":i==3?"queued":"running",Elapsed=TimeSpan.FromSeconds(i==0?8:12),Usage=new(240+i*100,80+i*50,i==2),Output="Chunk completed and cross-reviewed. Workspace permission checks passed." });
            var item=new TeamItem(); var usage=vm.Services.Usage; foreach(var model in vm.Services.AutoModels().Take(4)) { usage.Start(model); usage.Complete(model,new(240,80)); } item.Refresh(job,usage); chat.Items.Add(item);
            _stickToBottom = false;
            Dispatcher.BeginInvoke(() => ChatScroll.ScrollToTop(), DispatcherPriority.ApplicationIdle);
            SetSidebar(false,false); return;
        }
        if (page is "composer-modes" or "plan-card" or "approval-card" or "changes-panel") {
            WelcomeOverlay.Visibility = Visibility.Collapsed;
            chat.Workspace = @"C:\Projects\OrchDesk";
            chat.SelectedProvider = chat.ProviderOptions[0];
            chat.Items.Clear();
            chat.PermissionIndex = page is "composer-modes" or "changes-panel" ? 1 : 0;
            chat.WorkModeIndex = page == "plan-card" ? 0 : 1;
            chat.Input = page == "composer-modes" ? "Build the composer modes and verify the tests." : "";
            chat.Items.Add(new UserItem("Add workspace permissions and a planning mode to the composer."));
            if (page == "plan-card") chat.Items.Add(new PlanItem(["Inspect the composer and permission policy.", "Add workspace permissions, approvals and undo history.", "Build and run the full test suite."], _ => Task.CompletedTask));
            if (page == "approval-card") chat.Items.Add(new ApprovalItem(new("edit_file", "Edit composer settings", "Ask first · review this change", "--- composer.cs\n+++ composer.cs\n- mode = Chat;\n+ mode = Agent;")));
            if (page == "changes-panel") {
                chat.Changes.Add(new(new(chat.EffectiveWorkspace, "composer.cs", "edit_file", System.Text.Encoding.UTF8.GetBytes("mode = Chat;"), System.Text.Encoding.UTF8.GetBytes("mode = Agent;"))));
                chat.Changes.Add(new(new(chat.EffectiveWorkspace, "legacy.cs", "delete_file", System.Text.Encoding.UTF8.GetBytes("// retired"), null)));
                chat.ChangesVisible = true;
            }
            SetSidebar(false, false);
            return;
        }
        if (page is "composer-empty" or "composer-ready")
        {
            WelcomeOverlay.Visibility = Visibility.Collapsed;
            chat.Workspace = null;
            chat.Items.Clear();
            chat.Input = page == "composer-ready" ? "Help me plan my next project." : "";
            SetSidebar(false, false);
            return;
        }
        if (page is "thread" or "sidebar-expanded" or "sidebar-collapsed") Height = 860;
        if (!vm.Services.UseDemo)
        {
            WelcomeOverlay.Visibility = Visibility.Collapsed;
            SetSidebar(page == "sidebar-collapsed", false);
            if (page is "selector" or "picker") OpenModelPicker();
            PrepareRedesignScreenshot(page);
            return;
        }
        chat.Workspace = @"C:\Projects\OrchDesk";
        chat.SelectedProvider = chat.ProviderOptions.FirstOrDefault(p => p.Model == "sonnet") ?? chat.SelectedProvider;
        PrepareFollowupScreenshot(page);
        if (page is "thread" or "sidebar-expanded" or "sidebar-collapsed") {
            chat.Items.Add(new UserItem("Investigate the latency regression and suggest a fix."));
            chat.Items.Add(new ToolItem("demo", "query_metrics", "{ \"period\": \"7 days\", \"metric\": \"p95 latency\" }") { Output = "p95: 180ms → 340ms\nSequential scan on events. Missing index: (account_id, created_at).", IsRunning = false, IsExpanded = true });
            chat.Items.Add(new AssistantItem("Claude Sonnet") { Reasoning = "Compare recent timings with the deployment, then inspect the query plan.", Text = "The increase lines up with Tuesday’s deploy. Adding an index should bring p95 back down without a rollback.", IsStreaming = true });
        }
        if (page == "reply") { chat.Items.Add(new UserItem("Reply with exactly: pong")); chat.Items.Add(new AssistantItem("Qwen 3.5 9B") { ProviderId = "local-pc", Text = "pong" }); }
        if (page == "thinking") { chat.Items.Add(new UserItem("Find the best approach for this project.")); chat.IsBusy = true; chat.IsThinking = true; }
        if (page == "composer") chat.Input = "Review the chat experience in both themes.\n\nMake the input feel natural as the message grows.\nKeep the model selector close to the send action.\nThen run the build and all tests.";
        SetSidebar(page == "sidebar-collapsed", false);
        if (page is "selector" or "picker") OpenModelPicker();
        if (page is "thread" or "sidebar-expanded" or "sidebar-collapsed") { _stickToBottom = false; Dispatcher.BeginInvoke(() => { ChatScroll.ScrollToTop(); JumpLatest.Visibility = Visibility.Collapsed; }, DispatcherPriority.ContextIdle); }
        PrepareRedesignScreenshot(page);
        if (_modal is null) { FocusManager.SetFocusedElement(this, ModelPickerButton); ModelPickerButton.Focus(); }
    }
    private void Composer_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter || Keyboard.Modifiers.HasFlag(ModifierKeys.Shift)) return;
        e.Handled = true;
        if (DataContext is MainViewModel vm) vm.Chat.TrySend();
    }
}
