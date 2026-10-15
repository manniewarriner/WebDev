using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using OrchDesk.App.Services;
using OrchDesk.App.ViewModels;
using OrchDesk.Core.Activity;
using OrchDesk.Core.Config;
using OrchDesk.Core.Filtering;
using OrchDesk.Core.Providers;
namespace OrchDesk.App;

public partial class MainWindow
{
    private readonly Dictionary<ChatItem, string> _historyIds = [];
    private bool _historyDirty;
    private readonly System.Windows.Threading.DispatcherTimer _historyTimer = new() { Interval = TimeSpan.FromSeconds(1) };
    private void InitializeFollowup()
    {
        ModelFilters.Fields["Provider"] = [];
        ModelFilters.Fields["Location"] = ["Local", "Cloud"]; ModelFilters.Fields["Price"] = ["Free", "Paid"];
        ModelFilters.Changed += () => SearchModels(ModelSearch, null!);
        HistoryFilters.Fields["Title"] = ["Review", "Investigate", "Build"]; HistoryFilters.Fields["Date"] = [DateTime.Today.ToString("yyyy-MM-dd"), DateTime.Today.AddDays(-1).ToString("yyyy-MM-dd")]; HistoryFilters.Fields["Model"] = ["Claude Sonnet", "Qwen", "Gemma"];
        HistoryFilters.Changed += FilterHistory;
        JobFilters.Fields["Status"] = ["Running", "Completed", "Failed"]; JobFilters.Fields["Tool"] = ["run_command", "read_file", "write_file"]; JobFilters.Fields["Date"] = [DateTime.Today.ToString("yyyy-MM-dd")];
        JobFilters.Changed += FilterJobs;
        _historyTimer.Tick += (_, _) => PersistHistory();
        DataContextChanged += (_, _) =>
        {
            if (DataContext is not MainViewModel vm) return;
            ModelFilters.Fields["Provider"] = vm.Chat.ProviderOptions.Select(p => p.ProviderGroup).Distinct().ToArray();
            HomeActivity.Populate(vm.Services.UseDemo ? Enumerable.Range(0, 182).Select(i => new ActivityDay(DateOnly.FromDateTime(DateTime.Today).AddDays(i - 181), 0, 0)).ToArray() : ActivityAggregator.Read(vm.Services.Store.Directory, DateOnly.FromDateTime(DateTime.Today)));
            WelcomeOverlay.Visibility = !vm.Services.IsScreenshot && !vm.Services.Config.FirstRunCompleted ? Visibility.Visible : Visibility.Collapsed;
            if (!vm.Services.UseDemo) { HistorySkeleton.Visibility = Visibility.Visible; Dispatcher.BeginInvoke(() => { try { LoadHistory(); } finally { HistorySkeleton.Visibility = Visibility.Collapsed; } }, System.Windows.Threading.DispatcherPriority.Background); if (!vm.Services.IsScreenshot) _historyTimer.Start(); }
            vm.Accounts.PropertyChanged += (_, _) => RefreshAvatar(); vm.Services.Rebuilt += RefreshAvatar;
            vm.Chat.ContentAdded += () => _historyDirty = true;
            vm.Chat.Items.CollectionChanged += (_, change) => {
                ChatBackdrop.Dimmed = vm.Chat.Items.Count > 0;
                ChatBackdrop.InvalidateVisual();
                if(change.NewItems is not null) foreach(var tool in change.NewItems.OfType<ToolItem>()) tool.PropertyChanged += (_,_)=>FilterJobs();
                // Refresh after CollectionView has processed the collection notification.
                // Refreshing within Add can otherwise display the new row twice.
                Dispatcher.BeginInvoke(FilterJobs, System.Windows.Threading.DispatcherPriority.Background);
            };
            RefreshAvatar();
        };
        _history.CollectionChanged += (_, _) => { _historyDirty=true; FilterHistory(); };
        Closed += (_, _) => { PersistHistory(); _historyTimer.Stop(); };
        Activated += (_, _) => RefreshAvatar();
    }
    private string? ModelField(ModelPickerOption option, string field)
    {
        var profile = ModelProfiles.Get(option.Model ?? option.DisplayModel);
        var local = (DataContext as MainViewModel)?.Services.Config.Providers.FirstOrDefault(p => p.Id == option.Id)?.IsLocal ?? profile.IsLocal;
        return field switch { "Provider" => option.ProviderGroup, "Location" => local ? "Local" : "Cloud", "Price" => local || profile.IsFree ? "Free" : "Paid", _ => null };
    }
    private void HistorySearchChanged(object sender, TextChangedEventArgs e) { if (HistoryFilters is not null) FilterHistory(); }
    private void FilterHistory() { if (HistoryList is null) return; HistoryFilters.Fields["Model"]=_history.Select(h=>h.Model).Where(m=>!string.IsNullOrWhiteSpace(m)).Distinct().ToArray(); var predicate = FilterModel.BuildPredicate<ChatSnapshot>(HistoryFilters.Filters, (h, f) => f switch { "Title" => h.Title, "Date" => h.Date.ToString("yyyy-MM-dd"), "Model" => h.Model, _ => null }); HistoryList.ItemsSource = _history.Where(h => h.Title.Contains(HistorySearch.Text, StringComparison.OrdinalIgnoreCase) && predicate(h)).ToArray(); }
    private void FilterJobs() { if (DataContext is not MainViewModel vm) return; var view = CollectionViewSource.GetDefaultView(vm.Chat.Items); var predicate = FilterModel.BuildPredicate<ToolItem>(JobFilters.Filters, (t, f) => f switch { "Status" => t.IsRunning ? "Running" : t.IsError ? "Failed" : "Completed", "Tool" => t.Title, "Date" => t.Timestamp.ToString("yyyy-MM-dd"), _ => null }); view.Filter = o => o is not ToolItem tool || predicate(tool); JobFilters.Visibility = vm.Chat.Items.OfType<ToolItem>().Any() ? Visibility.Visible : Visibility.Collapsed; }
    private void CompleteWelcome(object sender, RoutedEventArgs e) { FinishWelcome(); OpenSettingsTab(); }
    private void SkipWelcome(object sender, RoutedEventArgs e) => FinishWelcome();
    private void FinishWelcome() { WelcomeOverlay.Visibility = Visibility.Collapsed; if (DataContext is MainViewModel vm) { vm.Services.Config.FirstRunCompleted = true; if (!vm.Services.IsScreenshot) vm.Services.Store.Save(vm.Services.Config); } }
    private void OpenSettingsTab() { if (DataContext is MainViewModel vm) new Views.SettingsWindow(new SettingsViewModel(vm.Services)) { Owner = this }.ShowDialog(); }
    private void RefreshAvatar() { if (DataContext is not MainViewModel vm) return; var accounts = vm.Services.Rotator.Accounts; var local = accounts.Any(a => a.Provider.IsLocal && a.Health == ProviderHealth.Online); var cloud = accounts.Any(a => !a.Provider.IsLocal && a.Health == ProviderHealth.Online); var status = local ? "Local models connected" : cloud ? "Cloud connected" : "Offline"; AvatarRing.SetResourceReference(Border.BorderBrushProperty, local ? "SuccessBrush" : cloud ? "WarnBrush" : "MutedBrush"); SidebarProfileName.Text = vm.Services.Config.DisplayName; ProfileStatus.Text = status; AvatarInitials.Text = vm.Services.Config.Initials; AvatarButton.ToolTip = vm.Services.Config.DisplayName + " · " + status; var path = Path.Combine(vm.Services.Store.Directory, "avatar.png"); if (!vm.Services.UseDemo && File.Exists(path)) { try { var bitmap = new BitmapImage(); bitmap.BeginInit(); bitmap.CacheOption = BitmapCacheOption.OnLoad; bitmap.UriSource = new Uri(path); bitmap.EndInit(); bitmap.Freeze(); AvatarImage.Fill = new ImageBrush(bitmap) { Stretch = Stretch.UniformToFill }; AvatarInitials.Visibility = Visibility.Collapsed; } catch (IOException) { } catch (NotSupportedException) { } } }
    private void OpenAccountMenu(object sender, RoutedEventArgs e) { var menu = new ContextMenu(); void Add(string label, Action action) { var item = new MenuItem { Header = label }; item.Click += (_, _) => action(); menu.Items.Add(item); } Add("Settings", OpenSettingsTab); Add("Integrations", OpenSettingsTab); Add("Change picture", () => { if (DataContext is MainViewModel vm) ProfilePicture.Pick(vm.Services, this); RefreshAvatar(); }); Add("Reduce motion", () => { if (DataContext is not MainViewModel vm) return; vm.Services.Config.ReduceMotion = !(vm.Services.Config.ReduceMotion ?? false); Motion.ReduceMotion = vm.Services.Config.ReduceMotion; Motion.Refresh(); if (!vm.Services.IsScreenshot) vm.Services.Store.Save(vm.Services.Config); }); AvatarButton.ContextMenu = menu; menu.IsOpen = true; }
    private sealed record SavedMessage(string Role, string Text, DateTimeOffset Timestamp);
    private sealed record SavedChat(string Title, string Model, DateTimeOffset Date, SavedMessage[] Messages, string? Workspace = null, string? ProviderId = null);
    private void PersistHistory() { if (DataContext is not MainViewModel vm || vm.Services.IsScreenshot || vm.Chat.IsBusy || !_historyDirty) return; try { var directory = Path.Combine(vm.Services.Store.Directory, "history"); Directory.CreateDirectory(directory); foreach (var chat in _history) { var first = chat.Items.FirstOrDefault(); if (first is null) continue; if (!_historyIds.TryGetValue(first, out var id)) _historyIds[first] = id = Guid.NewGuid().ToString("N"); var messages = chat.Items.Where(i => i is UserItem or AssistantItem).Select(i => new SavedMessage(i is UserItem ? "user" : "assistant", i is UserItem u ? u.Text : ((AssistantItem)i).Text, i.Timestamp)).ToArray(); var saved = new SavedChat(chat.Title, chat.Model, chat.Date, messages, chat.Workspace, chat.ProviderId); var path = Path.Combine(directory, id + ".json"); var json = JsonSerializer.Serialize(saved); if (!File.Exists(path) || File.ReadAllText(path) != json) { File.WriteAllText(path + ".tmp", json); File.Move(path + ".tmp", path, true); } } _historyDirty=false; HomeActivity.Populate(ActivityAggregator.Read(vm.Services.Store.Directory,DateOnly.FromDateTime(DateTime.Today))); } catch (IOException) { } catch (UnauthorizedAccessException) { } }
    private void LoadHistory() { if (DataContext is not MainViewModel vm) return; var directory = Path.Combine(vm.Services.Store.Directory, "history"); if (!Directory.Exists(directory)) return; foreach (var path in Directory.EnumerateFiles(directory, "*.json").OrderByDescending(File.GetLastWriteTime).Take(8)) { try { var saved = JsonSerializer.Deserialize<SavedChat>(File.ReadAllText(path)); if (saved is null) continue; var items = saved.Messages.Select(m => m.Role == "user" ? (ChatItem)new UserItem(m.Text) { Timestamp = m.Timestamp } : new AssistantItem(saved.Model) { Text = m.Text, Timestamp = m.Timestamp }).ToArray(); if (items.Length > 0) _historyIds[items[0]] = Path.GetFileNameWithoutExtension(path); _history.Add(new(saved.Title, items, null) { Date = saved.Date, Model = saved.Model, Workspace = saved.Workspace, ProviderId = saved.ProviderId }); } catch (Exception ex) when (ex is IOException or JsonException) { } } }
    private void PrepareFollowupScreenshot(string? page) { if (DataContext is not MainViewModel vm) return; WelcomeOverlay.Visibility = page == "welcome" ? Visibility.Visible : Visibility.Collapsed; if (page == "heatmap") { vm.Chat.Workspace = null; HomeActivity.Populate(Enumerable.Range(0, 182).Select(i => new ActivityDay(DateOnly.FromDateTime(DateTime.Today).AddDays(i - 181), i % 7 == 0 ? 0 : (i * 17) % 19, i % 5 == 0 ? 2 : 0)).ToArray()); } if (page == "filters") { _history.Add(new("Review the chat experience", [new UserItem("Review the chat experience")], null) { Model = "Claude Sonnet" }); HistoryFilters.Add("Title", "Review", FilterOperator.Contains); ModelFilters.Add("Location", "Cloud"); ModelFilters.Add("Price", "Paid"); OpenModelPicker(); } if (page == "background") { vm.Chat.Items.Clear(); ChatBackdrop.Opacity = .12; } if (page is "sidebar-expanded" or "sidebar-collapsed") { AvatarRing.SetResourceReference(Border.BorderBrushProperty, "SuccessBrush"); AvatarButton.ToolTip = "MW · Local models connected"; } }
}
