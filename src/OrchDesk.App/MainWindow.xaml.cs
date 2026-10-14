using System.Windows;
using System.Windows.Input;
using OrchDesk.App.ViewModels;

namespace OrchDesk.App;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        DataContextChanged += (_, _) =>
        {
            if (DataContext is not MainViewModel vm) return;
            // Follow the conversation as it streams, unless the user has scrolled up to read.
            vm.Chat.ContentAdded += () =>
            {
                if (ChatScroll.VerticalOffset >= ChatScroll.ScrollableHeight - 80) ChatScroll.ScrollToEnd();
            };
        };
        Loaded += (_, _) => Composer.Focus();
    }

    /// <summary>Enter sends; Shift+Enter inserts a new line.</summary>
    private void Composer_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter || Keyboard.Modifiers.HasFlag(ModifierKeys.Shift)) return;
        e.Handled = true;
        if (DataContext is MainViewModel { Chat.SendCommand: var send } && send.CanExecute(null)) send.Execute(null);
    }
}
