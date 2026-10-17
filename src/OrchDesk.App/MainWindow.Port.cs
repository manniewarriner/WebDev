using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using OrchDesk.App.Services;
using OrchDesk.App.ViewModels;
using OrchDesk.App.Views;
using OrchDesk.Core.Knowledge;

namespace OrchDesk.App;

public partial class MainWindow
{
    private AutomationService? automation;
    private WindowStyle restoredStyle;
    private WindowState restoredState;
    private ResizeMode restoredResize;
    private bool fullscreen;
    private readonly ContextMenu slashPicker = new();
    private void InitializePortedFeatures()
    {
        DataContextChanged += (_, _) => {
            automation?.Dispose(); automation = null;
            if (DataContext is MainViewModel vm && !vm.Services.IsScreenshot && !vm.Services.ChatOnly)
            { automation = new(vm.Services, () => vm.Chat.IsBusy); automation.Start(); }
        };
        Closed += (_, _) => automation?.Dispose();
        PreviewKeyDown += (_, e) => {
            if (e.Key != Key.F11) return;
            if (!fullscreen) { restoredStyle = WindowStyle; restoredState = WindowState; restoredResize = ResizeMode; WindowStyle = WindowStyle.None; ResizeMode = ResizeMode.NoResize; WindowState = WindowState.Maximized; }
            else { WindowStyle = restoredStyle; ResizeMode = restoredResize; WindowState = restoredState; }
            fullscreen = !fullscreen; e.Handled = true;
        };
        Composer.TextChanged += (_, _) => {
            if (DataContext is not MainViewModel vm || !Composer.Text.StartsWith('/') || Composer.Text.Contains(' ')) { slashPicker.IsOpen = false; return; }
            var query = Composer.Text[1..]; slashPicker.Items.Clear();
            void Add(string label, Action action) { var item = new MenuItem { Header = label }; item.Click += (_, _) => { action(); Composer.Focus(); }; slashPicker.Items.Add(item); }
            foreach (var command in new[] { "plan", "overnight", "skills", "memory", "usage", "research" }.Where(c => c.StartsWith(query, StringComparison.OrdinalIgnoreCase)))
                Add("/" + command, () => { if (command == "plan") { vm.Chat.WorkModeIndex = 0; vm.Chat.Input = ""; } else if (command == "overnight") vm.Chat.Input = "/overnight "; else { vm.Chat.Input = ""; ShowPortedTools(command == "usage" ? "Usage" : command == "research" ? "Research" : "Skills & memory"); } });
            foreach (var skill in SkillLibrary.LoadFor(vm.Chat.EffectiveWorkspace, vm.Services.Config).Where(s => s.Name.Contains(query, StringComparison.OrdinalIgnoreCase)).Take(20))
                Add(skill.Name + " · " + skill.Description, () => { vm.Chat.SelectedSkills.Add(skill); vm.Chat.Input = ""; vm.Chat.ComposerNotice = "Skill selected: " + skill.Name; });
            slashPicker.PlacementTarget = Composer; slashPicker.Placement = PlacementMode.Top; slashPicker.IsOpen = slashPicker.Items.Count > 0;
        };
    }
    private void ShowPortedTools(string page)
    {
        if (DataContext is not MainViewModel vm) return;
        automation ??= new(vm.Services, () => vm.Chat.IsBusy);
        new WorkspaceToolsWindow(vm, automation, page) { Owner = this }.Show();
    }
    private void OpenPortedTools(object sender, RoutedEventArgs e) => ShowPortedTools("Automation");
    private void OpenPortedResearch(object sender, RoutedEventArgs e) => ShowPortedTools("Research");
    private void OpenPortedUsage(object sender, RoutedEventArgs e) => ShowPortedTools("Usage");
}
