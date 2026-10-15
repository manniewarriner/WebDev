using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Threading;
using OrchDesk.App;
using OrchDesk.App.Controls;
using OrchDesk.App.Services;
using OrchDesk.App.ViewModels;
using OrchDesk.Core.Config;

namespace OrchDesk.Tests;

public class ClickableAuditTests
{
    [Fact]
    public void MainWindowVisualTreeHasActionsAndNoninteractiveBackgrounds()
    {
        Exception? failure = null;
        var thread = new Thread(() => {
            try {
                SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext());
                var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
                foreach (var resource in new[] { "Themes/Dark.xaml", "Themes/Typography.xaml", "Themes/Spacing.xaml", "Themes/Radius.xaml", "Themes/Effects.xaml", "Themes/Styles.xaml", "Assets/ProviderLogos.xaml" })
                    app.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri("/OrchDesk;component/" + resource, UriKind.Relative) });
                app.Resources.Add("PageEquals", new PageEqualsConverter());
                app.Resources.Add("PageVisible", new PageVisibilityConverter());
                app.Resources.Add("BoolVisible", new BoolVisibilityConverter());
                app.Resources.Add("HasValue", new HasValueVisibilityConverter());
                app.Resources.Add("ZeroVisible", new ZeroVisibilityConverter());
                Motion.Screenshot = true;
                using var store = new ConfigStore(Path.Combine(Path.GetTempPath(), "OrchDesk-ui-" + Guid.NewGuid()));
                var services = new AppServices(true, store, true, chatOnly: true, providers: [new PongProvider()]);
                var vm = new MainViewModel(services, new SuppliedFolderPicker(Path.Combine(store.Directory, "picked")));
                var window = new MainWindow { DataContext = vm, Width = 900, Height = 680 };
                app.MainWindow = window; window.Show(); Pump(window);
                foreach (var name in new[] { "CommandsAction", "UsageAction", "FilesAction", "MoreActions" }) {
                    var control = (Control)window.FindName(name);
                    Assert.Equal(32, control.Height);
                }
                Assert.Equal(Visibility.Visible, ((Button)window.FindName("MoreActions")).Visibility);
                Directory.CreateDirectory(store.Directory);
                vm.Chat.Workspace = store.Directory;
                Directory.CreateDirectory(Path.Combine(store.Directory, "picked"));
                var selftest = window.RunUiSelftestAsync(store.Directory, Path.Combine(store.Directory, "picked"));
                var deadline = DateTime.UtcNow.AddSeconds(10);
                while (!selftest.IsCompleted && DateTime.UtcNow < deadline) { Pump(window); Thread.Sleep(10); }
                Assert.True(selftest.IsCompletedSuccessfully, $"UI selftest did not complete: busy={vm.Chat.IsBusy}, input={vm.Chat.Input.Length}, items={string.Join(",", vm.Chat.Items.Select(i => i.GetType().Name))}, blocked={vm.Chat.SendBlockedReason}");
                Assert.Contains("\"passed\":true", File.ReadAllText(Path.Combine(store.Directory, "ui-selftest.json")));
                foreach (var page in new[] { "home", "thinking", "reply", "picker", "palette", "connect-dialog", "overnight-setup", "usage", "file-tree", "diff", "code-block", "toasts", "welcome", "accounts", "delete-confirm", "delete-undo", "composer-modes" }) {
                    // Separate screenshot fixtures retain modal state; close them before the next surface.
                    typeof(MainWindow).GetMethod("CloseModal", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(window, null);
                    vm.Chat.IsBusy = false; vm.Chat.IsThinking = false; vm.Chat.Items.Clear(); vm.Page = "Chat";
                    window.PrepareScreenshot(page); Pump(window);
                    Audit(window);
                    if (page is "thinking" or "reply") {
                        Assert.Equal(Visibility.Collapsed, ((FrameworkElement)window.FindName("HomeContent")).Visibility);
                        Assert.Equal(Visibility.Visible, ((FrameworkElement)window.FindName("ChatScroll")).Visibility);
                    }
                }
                vm.Chat.IsBusy = false; vm.Chat.Items.Clear(); Pump(window);
                var snapshot = new MainWindow.ChatSnapshot("Delete test chat", [new UserItem("Delete test chat")], null);
                var history = (System.Collections.ObjectModel.ObservableCollection<MainWindow.ChatSnapshot>)typeof(MainWindow).GetField("_history", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(window)!;
                history.Add(snapshot);
                typeof(MainWindow).GetMethod("DeleteChat", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(window, [snapshot]); Pump(window);
                Assert.DoesNotContain(snapshot, history);
                Assert.DoesNotContain(snapshot, ((ItemsControl)window.FindName("HistoryList")).Items.Cast<object>());
                var undo = Walk(window, new HashSet<DependencyObject>()).OfType<Button>().Last(b => Equals(b.Content, "Undo"));
                undo.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent)); Pump(window);
                Assert.Contains(snapshot, history);
                Assert.Contains(snapshot, ((ItemsControl)window.FindName("HistoryList")).Items.Cast<object>());
                vm.Chat.Items.Clear(); Pump(window);
                Assert.Equal(ChatViewState.Home, vm.Chat.ViewState);
                foreach (var name in new[] { "MoreActions", "AvatarButton" }) {
                    var button = (Button)window.FindName(name);
                    button.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
                    Assert.NotNull(button.ContextMenu);
                    Audit(button.ContextMenu); button.ContextMenu.IsOpen = false;
                }
                var settings = new OrchDesk.App.Views.SettingsWindow(new SettingsViewModel(services)) { Owner = window };
                settings.Show();
                foreach (var page in new[] { "integrations", "settings-appearance", "settings-about", "settings-fallback", "settings-overnight", "settings-limits" }) { settings.SelectPage(page); Pump(settings); Audit(settings); }
                settings.Close();
                window.Close(); app.Shutdown();
            } catch (Exception ex) { failure = ex; }
        });
        thread.SetApartmentState(ApartmentState.STA); thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(60)), "STA audit timed out");
        if (failure is not null) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw();
    }

    private sealed class PongProvider : OrchDesk.Core.Providers.IModelProvider
    {
        public string Id => "fake";
        public string DisplayName => "Fake";
        public bool IsAgentic => false;
        public bool IsLocal => true;
        public Task<OrchDesk.Core.Providers.ProviderHealth> CheckHealthAsync(CancellationToken ct) => Task.FromResult(OrchDesk.Core.Providers.ProviderHealth.Online);
        public async IAsyncEnumerable<OrchDesk.Core.Models.ChatEvent> StreamChatAsync(OrchDesk.Core.Models.ChatRequest request, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct)
        {
            await Task.Delay(20, ct);
            yield return new OrchDesk.Core.Models.TextDelta("pong");
            yield return new OrchDesk.Core.Models.Done();
        }
    }

    private static void Pump(Window window) {
        window.UpdateLayout();
        var frame = new DispatcherFrame();
        window.Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, new Action(() => frame.Continue = false));
        Dispatcher.PushFrame(frame);
        window.UpdateLayout();
    }
    private static void Audit(DependencyObject root)
    {
        foreach (var element in Walk(root, new HashSet<DependencyObject>())) {
            if (element is EvervaultBackground or AuroraBackground) Assert.False(((UIElement)element).IsHitTestVisible);
            if (element is Button button) Assert.True(button.Command is not null || HasClick(button, System.Windows.Controls.Primitives.ButtonBase.ClickEvent), $"Dead button: {button.Name} {button.Content}");
            if (element is MenuItem menu) Assert.True(menu.Command is not null || HasClick(menu, MenuItem.ClickEvent), $"Dead menu: {menu.Header}");
            if (element is Hyperlink link) Assert.True(link.Command is not null || HasClick(link, Hyperlink.ClickEvent), "Dead hyperlink");
        }
    }
    private static bool HasClick(DependencyObject element, RoutedEvent click)
    {
        var owner = element is ContentElement ? typeof(ContentElement) : typeof(UIElement);
        var store = owner.GetProperty("EventHandlersStore", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(element);
        var handlers = store?.GetType().GetMethod("GetRoutedEventHandlers", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)!.Invoke(store, [click]) as Array;
        return handlers is { Length: > 0 };
    }
    private static IEnumerable<DependencyObject> Walk(DependencyObject root, HashSet<DependencyObject> seen)
    {
        if (!seen.Add(root)) yield break;
        yield return root;
        if (root is Visual || root is System.Windows.Media.Media3D.Visual3D)
            for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
                foreach (var child in Walk(VisualTreeHelper.GetChild(root, i), seen)) yield return child;
        foreach (var logical in LogicalTreeHelper.GetChildren(root).OfType<DependencyObject>())
            foreach (var child in Walk(logical, seen)) yield return child;
    }
}
