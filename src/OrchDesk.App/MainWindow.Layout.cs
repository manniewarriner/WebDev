using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using OrchDesk.App.ViewModels;
using OrchDesk.App.Services;
using OrchDesk.Core.Presentation;

namespace OrchDesk.App;
public partial class MainWindow
{
    private readonly HashSet<ChatItem> _deletedChats = [];
    private void DeleteHistoryClick(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: ChatSnapshot chat }) ConfirmDeleteChat(chat);
    }
    private void DeleteHistoryMenu(object sender, RoutedEventArgs e)
    {
        if (sender is MenuItem { Parent: ContextMenu { PlacementTarget: FrameworkElement { DataContext: ChatSnapshot chat } } }) ConfirmDeleteChat(chat);
    }
    private void ConfirmDeleteChat(ChatSnapshot chat)
    {
        if (DataContext is not MainViewModel vm || vm.Chat.IsBusy) { ShowToast("Wait for the current response to finish before deleting a chat.", false); return; }
        var body = ShowModal("Delete chat?", 440);
        body.Children.Add(Label(chat.Title));
        body.Children.Add(Label("This chat will be removed. You can undo for 6 seconds.", "Muted", 13));
        var actions = new StackPanel { Orientation = Orientation.Horizontal };
        actions.Children.Add(ActionButton("Cancel", DismissModal));
        actions.Children.Add(ActionButton("Delete chat", () => { CloseModal(); DeleteChat(chat); }, true));
        body.Children.Add(actions);
    }
    private void DeleteChat(ChatSnapshot chat)
    {
        if (DataContext is not MainViewModel vm) return;
        PersistHistory();
        var first = chat.Items.FirstOrDefault();
        var storage = new ChatDeletion(Path.Combine(vm.Services.Store.Directory, "history"));
        string? trash = null;
        try { if (!vm.Services.IsScreenshot && first is not null && _historyIds.TryGetValue(first, out var id)) trash = storage.MoveToTrash(id); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { ShowToast("Unable to delete chat. Please try again.", true); return; }
        var index = Math.Max(0, _history.IndexOf(chat));
        if (first is not null) _deletedChats.Add(first);
        _history.Remove(chat);
        var active = first is not null && vm.Chat.Items.FirstOrDefault() == first;
        if (active) vm.Chat.Restore([], null);
        var row = new DockPanel();
        var toast = Card(row); toast.HorizontalAlignment = HorizontalAlignment.Right; toast.VerticalAlignment = VerticalAlignment.Center; toast.Margin = (Thickness)FindResource("Inset.0_0_0_8"); toast.Padding = (Thickness)FindResource("Inset.16"); toast.MaxWidth = 360;
        var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(6) };
        var undo = ActionButton("Undo", () => {
            try { if (trash is not null) storage.Restore(trash); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { ShowToast("Unable to restore chat. Please try again.", true); return; }
            timer.Stop(); DeleteToastHost.Children.Remove(toast);
            if (first is not null) _deletedChats.Remove(first);
            _history.Insert(Math.Min(index, _history.Count), chat);
            if (active && vm.Chat.Items.Count == 0 && !vm.Chat.IsBusy) vm.Chat.Restore(chat.Items, chat.Conversation);
        });
        undo.Margin = (Thickness)FindResource("Inset.16_0_0_0"); undo.Height = undo.MinHeight = 32; undo.Padding = (Thickness)FindResource("Inset.12_4");
        DockPanel.SetDock(undo, Dock.Right); row.Children.Add(undo); var caption = Label("Chat deleted"); caption.VerticalAlignment = VerticalAlignment.Center; row.Children.Add(caption); DeleteToastHost.Children.Add(toast);
        timer.Tick += (_, _) => { timer.Stop(); DeleteToastHost.Children.Remove(toast); };
        if (!Motion.Screenshot) timer.Start();
    }
    private void ApplyLayout()
    {
        // The shell toolbar is one compact, consistently sized control group.
        foreach (var control in new Control[] { CommandsAction, UsageAction, FilesAction, MoreActions }) { control.Height = 32; control.MinHeight = 32; control.Padding = (Thickness)FindResource("Inset.12_4"); control.VerticalAlignment = VerticalAlignment.Top; }
        MoreActions.Visibility = Visibility.Visible;
    }
}
