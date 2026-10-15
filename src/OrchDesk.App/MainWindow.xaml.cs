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

    public void OpenModelPicker() => ModelPickerOverlay.Visibility = Visibility.Visible;

    private void ToggleModelPicker(object sender, RoutedEventArgs e) =>
        ModelPickerOverlay.Visibility = ModelPickerOverlay.IsVisible ? Visibility.Collapsed : Visibility.Visible;

    private void ModelPicked(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (IsLoaded) ModelPickerOverlay.Visibility = Visibility.Collapsed;
    }

    private void DismissModelPicker(object sender, MouseButtonEventArgs e)
    {
        if (ModelPickerOverlay.IsVisible && !ModelPickerOverlay.IsMouseOver && !ModelPickerButton.IsMouseOver)
            ModelPickerOverlay.Visibility = Visibility.Collapsed;
    }

    /// <summary>Enter sends; Shift+Enter inserts a new line.</summary>
    private void Composer_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter || Keyboard.Modifiers.HasFlag(ModifierKeys.Shift)) return;
        e.Handled = true;
        if (DataContext is MainViewModel { Chat.SendCommand: var send } && send.CanExecute(null)) send.Execute(null);
    }
}
