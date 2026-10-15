using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Controls;
using System.Windows.Media;
using OrchDesk.App.ViewModels;

namespace OrchDesk.App;

public partial class MainWindow
{
    public async Task<bool> RunUiSelftestAsync(string configDirectory, string? folder)
    {
        var stage = "setup";
        var sent = false; var replied = false; var picked = false;
        try
        {
            if (DataContext is not MainViewModel vm || folder is null || !Directory.Exists(folder)) throw new InvalidOperationException();
            WelcomeOverlay.Visibility = Visibility.Collapsed;
            await vm.Chat.NewChatCommand.ExecuteAsync(null);
            stage = "composer";
            var editor = new TextBoxAutomationPeer(Composer);
            ((IValueProvider)editor.GetPattern(PatternInterface.Value)).SetValue("Reply with exactly: pong");
            await Dispatcher.InvokeAsync(() => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
            stage = "send";
            var send = Descendants(this).OfType<Button>().Single(b => Equals(b.Content, "Send"));
            stage = "send-enabled";
            if (!send.IsEnabled) throw new InvalidOperationException();
            stage = "send-invoke";
            ((IInvokeProvider)new ButtonAutomationPeer(send).GetPattern(PatternInterface.Invoke)).Invoke();
            stage = "reply";
            var deadline = DateTime.UtcNow.AddSeconds(120);
            while (DateTime.UtcNow < deadline)
            {
                sent |= vm.Chat.Items.OfType<UserItem>().Any();
                replied = vm.Chat.Items.OfType<AssistantItem>().Any(a => !a.IsStreaming && !string.IsNullOrWhiteSpace(a.Text));
                if (replied && !vm.Chat.IsBusy) break;
                await Task.Delay(100);
            }
            if (!vm.Chat.IsBusy) {
                stage = "folder";
                var button = Descendants(this).OfType<Button>().First(b => b.Command == vm.Chat.PickFolderCommand);
                ((IInvokeProvider)new ButtonAutomationPeer(button).GetPattern(PatternInterface.Invoke)).Invoke();
                await Dispatcher.InvokeAsync(() => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
                picked = string.Equals(vm.Chat.Workspace, Path.GetFullPath(folder), StringComparison.OrdinalIgnoreCase);
            }
        }
        catch (Exception ex) { stage += ":" + ex.GetType().Name; /* Never serialize exception messages or provider content. */ }
        var passed = sent && replied && picked;
        Directory.CreateDirectory(configDirectory);
        await File.WriteAllTextAsync(Path.Combine(configDirectory, "ui-selftest.json"), JsonSerializer.Serialize(new { passed, stage, sent, assistantReply = replied, workspaceChanged = picked }));
        return passed;
    }

    private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        yield return root;
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
            foreach (var child in Descendants(VisualTreeHelper.GetChild(root, i))) yield return child;
    }
}
