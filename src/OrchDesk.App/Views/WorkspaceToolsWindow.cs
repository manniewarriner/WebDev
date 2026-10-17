using System.IO;
using System.Windows;
using System.Windows.Controls;
using OrchDesk.App.Services;
using OrchDesk.App.ViewModels;
using OrchDesk.Core.Automation;
using OrchDesk.Core.Dashboard;
using OrchDesk.Core.Knowledge;
using OrchDesk.Core.Research;

namespace OrchDesk.App.Views;

/// <summary>The baseline's management pages, sharing the branch's services and permission gate.</summary>
public sealed class WorkspaceToolsWindow : Window
{
    private readonly MainViewModel vm;
    private readonly AutomationService automation;
    private readonly TabControl tabs = new();
    private readonly TextBlock notice = new() { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(8) };
    public WorkspaceToolsWindow(MainViewModel vm, AutomationService automation, string page = "Automation")
    {
        this.vm = vm; this.automation = automation;
        Title = "OrchDesk · Workspace tools"; Width = 1000; Height = 760;
        SetResourceReference(BackgroundProperty, "BaseBrush"); SetResourceReference(ForegroundProperty, "TextBrush");
        var dock = new DockPanel(); DockPanel.SetDock(notice, Dock.Bottom); dock.Children.Add(notice); dock.Children.Add(tabs); Content = dock;
        AddTab("Automation", Automation()); AddTab("Skills & memory", Knowledge()); AddTab("Tasks & reports", Tasks()); AddTab("Research", new ResearchView { DataContext = vm });
        AddTab("Usage", new UsageView { DataContext = vm });
        AddTab("Files", new FileExplorer { RootPath = vm.Chat.EffectiveWorkspace });
        AddTab("Sessions", Sessions());
        tabs.SelectedIndex = page == "Usage" ? 4 : page == "Research" ? 3 : page == "Skills & memory" ? 1 : 0;
    }
    private void AddTab(string title, UIElement body) => tabs.Items.Add(new TabItem { Header = title, Content = body is StackPanel ? new ScrollViewer { Content = body, VerticalScrollBarVisibility = ScrollBarVisibility.Auto } : body });
    private static StackPanel Panel() => new() { Margin = new Thickness(20) };
    private static TextBox Field(Panel panel, string label, string value = "") { panel.Children.Add(new TextBlock { Text = label, Margin = new Thickness(0, 10, 0, 4) }); var box = new TextBox { Text = value, MinHeight = 32 }; panel.Children.Add(box); return box; }
    private void Button(Panel panel, string title, Action action) { var b = new Button { Content = title, Margin = new Thickness(0, 6, 0, 0), HorizontalAlignment = HorizontalAlignment.Left }; b.Click += (_, _) => { try { action(); } catch (Exception e) { notice.Text = e.Message; } }; panel.Children.Add(b); }
    private void Toggle(Panel panel, string title, bool value, Action<bool> change) { var box = new CheckBox { Content = title, IsChecked = value, Margin = new Thickness(0, 8, 0, 0) }; box.Click += (_, _) => { change(box.IsChecked == true); vm.Services.Store.Save(vm.Services.Config); }; panel.Children.Add(box); }
    private UIElement Automation()
    {
        var p = Panel(); var s = vm.Services; var c = s.Config;
        Toggle(p, "Night shift (use the saved night ladder)", c.NightShift, value => c.NightShift = value);
        var goal = Field(p, "Goal"); var project = Field(p, "Project folder", vm.Chat.EffectiveWorkspace);
        var continuous = new CheckBox { Content = "Continuous run (respects configured run, step and time caps)", IsChecked = false }; p.Children.Add(continuous);
        var queue = new ListBox { MinHeight = 80, DisplayMemberPath = "Summary", ItemsSource = automation.Supervisor.Tasks };
        Button(p, "Queue goal", () => { automation.Supervisor.Submit(goal.Text, project.Text, vm.Chat.SelectedProvider?.Id ?? "", continuous.IsChecked == true); queue.ItemsSource = automation.Supervisor.Tasks; });
        Button(p, "Start / resume queue", automation.Supervisor.Start); Button(p, "Pause queue", automation.Supervisor.Pause); Button(p, "Stop queue", automation.Supervisor.Stop); p.Children.Add(queue);
        var time = Field(p, "Daily schedule time (HH:mm)", "09:00");
        var auto = new CheckBox { Content = "Allow routine unattended work (otherwise plan only)" }; p.Children.Add(auto);
        var schedules = new ListBox { ItemsSource = s.Schedules.Jobs, MinHeight = 70 };
        Button(p, "Add daily schedule", () => { s.Schedules.Add(new() { Name = goal.Text[..Math.Min(goal.Text.Length, 60)], Prompt = goal.Text, Project = project.Text, Time = time.Text, Mode = auto.IsChecked == true ? ScheduleMode.Auto : ScheduleMode.Plan }); schedules.ItemsSource = s.Schedules.Jobs; });
        Button(p, "Pause / resume selected schedule", () => { if (schedules.SelectedItem is ScheduledJob j) { s.Schedules.SetEnabled(j.Id, !j.Enabled); schedules.Items.Refresh(); } });
        Button(p, "Delete selected schedule", () => { if (schedules.SelectedItem is ScheduledJob j) { s.Schedules.Remove(j.Id); schedules.ItemsSource = s.Schedules.Jobs; } }); p.Children.Add(schedules);
        var ceiling = Field(p, "Daily unattended token ceiling (0 = unlimited)", c.DailyTokenCeiling.ToString());
        var rate = Field(p, "Remote runs per hour", c.RemoteMaxPerHour.ToString());
        Button(p, "Save limits", () => { c.DailyTokenCeiling = Math.Max(0, long.Parse(ceiling.Text)); c.RemoteMaxPerHour = Math.Clamp(int.Parse(rate.Text), 1, 100); s.Store.Save(c); });
        var token = new PasswordBox { Margin = new Thickness(0, 12, 0, 0), ToolTip = "Telegram bot token from BotFather" }; p.Children.Add(token);
        Button(p, "Save Telegram bot token", () => { if (token.Password.Length > 0) s.SecretFile.Set("telegram", token.Password); token.Clear(); automation.RestartTelegram(); });
        Toggle(p, "Enable Telegram", c.TelegramEnabled, value => { c.TelegramEnabled = value; automation.RestartTelegram(); });
        Toggle(p, "Allow remote use of this folder's Full access setting", c.RemoteAllowAuto, value => c.RemoteAllowAuto = value);
        Button(p, "Pair a chat", () => { if (automation.Telegram is not { } t) throw new InvalidOperationException("Save a token and enable Telegram first."); notice.Text = "Send /pair " + t.NewPairingCode() + " to your bot within 10 minutes."; });
        Button(p, "Unpair chat", () => { c.TelegramChatId = 0; s.Store.Save(c); automation.RestartTelegram(); notice.Text = "Chat unpaired."; });
        Button(p, "Refresh status", () => { queue.ItemsSource = automation.Supervisor.Tasks; notice.Text = automation.StatusText() + "\n" + automation.Telegram?.Status + "\n" + automation.Telegram?.Notice; });
        Button(p, "Undo last automation file change", () => { var entry = automation.Changes.Entries.LastOrDefault(e => e.CanUndo); if (entry is not null) { entry.Undo(); notice.Text = "Undid " + entry.Path; } });
        return p;
    }
    private UIElement Knowledge()
    {
        var p = Panel(); var c = vm.Services.Config;
        Toggle(p, "Use skills", c.Skills, v => c.Skills = v); Toggle(p, "Load project skills", c.SkillsFromProject, v => c.SkillsFromProject = v);
        Toggle(p, "Load Claude skills", c.SkillsFromClaude, v => { c.SkillsFromClaude = v; c.ClaudeSkillsEnabledOnce |= v; });
        Toggle(p, "Use remembered facts", c.Memory, v => c.Memory = v);
        var skills = new ListBox { ItemsSource = SkillLibrary.LoadFor(vm.Chat.EffectiveWorkspace, c), DisplayMemberPath = "Name", Height = 140 }; p.Children.Add(skills);
        Button(p, "Use selected skill in chat", () => { if (skills.SelectedItem is Skill skill) { vm.Chat.SelectedSkills.Add(skill); notice.Text = "Selected " + skill.Name; } });
        var notes = new ListBox { ItemsSource = vm.Services.Memory.List(vm.Chat.EffectiveWorkspace), DisplayMemberPath = "Text", Height = 160 }; p.Children.Add(notes);
        var text = Field(p, "Fact or preference (up to 400 characters)");
        Button(p, "Remember in this project", () => { vm.Services.Memory.Add(vm.Chat.EffectiveWorkspace, text.Text, source: "user"); notes.ItemsSource = vm.Services.Memory.List(vm.Chat.EffectiveWorkspace); });
        Button(p, "Delete selected note", () => { if (notes.SelectedItem is MemoryNote n) { vm.Services.Memory.Delete(n.Id); notes.ItemsSource = vm.Services.Memory.List(vm.Chat.EffectiveWorkspace); } }); return p;
    }
    private UIElement Tasks()
    {
        var p = Panel(); var s = vm.Services; var title = Field(p, "Task title"); var project = Field(p, "Project label"); var due = Field(p, "Due date (yyyy-MM-dd, optional)");
        var list = new ListBox { ItemsSource = s.Tasks.List(), DisplayMemberPath = "Title", Height = 180 }; p.Children.Add(list);
        Button(p, "Add task", () => { s.Tasks.Add(title.Text, project.Text, "P2", due.Text, "", "app"); list.ItemsSource = s.Tasks.List(); });
        Button(p, "Mark in progress", () => { if (list.SelectedItem is TaskItem t) { s.Tasks.Update(t.Id, new(Status: "in_progress")); list.ItemsSource = s.Tasks.List(); } });
        Button(p, "Complete task", () => { if (list.SelectedItem is TaskItem t) { s.Tasks.Update(t.Id, new(Status: "done")); list.ItemsSource = s.Tasks.List(); } });
        Button(p, "Delete task", () => { if (list.SelectedItem is TaskItem t) { s.Tasks.Delete(t.Id); list.ItemsSource = s.Tasks.List(); } });
        var reports = new ListBox { ItemsSource = s.Reports.List(), DisplayMemberPath = "Title", Height = 100 }; p.Children.Add(reports);
        var body = new TextBox { IsReadOnly = true, TextWrapping = TextWrapping.Wrap, MinHeight = 100 }; p.Children.Add(body); reports.SelectionChanged += (_, _) => body.Text = (reports.SelectedItem as ReportItem)?.Body ?? "";
        var port = Field(p, "Local API port", "18765");
        Button(p, "Generate API key", () => { var key = OrchDesk.Core.Dashboard.DashboardApi.NewKey(); s.SecretFile.Set("dashboard-api", key); body.Text = "API key (copy now): " + key; });
        Button(p, "Start local API", () => { automation.Api.Start(int.Parse(port.Text)); notice.Text = automation.Api.Problem ?? $"Listening on http://127.0.0.1:{automation.Api.Port}/api/"; });
        Button(p, "Stop local API", automation.Api.Stop); return p;
    }
    private UIElement Sessions()
    {
        var p = Panel(); var directory = Path.Combine(vm.Services.Store.Directory, "history");
        var chats = new List<SavedChat>();
        if (Directory.Exists(directory)) foreach (var path in Directory.EnumerateFiles(directory, "*.json"))
        {
            try { if (System.Text.Json.JsonSerializer.Deserialize<SavedChat>(File.ReadAllText(path)) is { } chat) chats.Add(chat); }
            catch (Exception e) when (e is IOException or System.Text.Json.JsonException) { }
        }
        var list = new ListBox { ItemsSource = chats.OrderByDescending(c => c.Date), DisplayMemberPath = "Title", Height = 180 }; p.Children.Add(list);
        Button(p, "Restore selected chat", () => {
            if (vm.Chat.IsBusy || list.SelectedItem is not SavedChat chat) return;
            vm.Chat.Workspace = chat.Workspace; vm.Chat.Restore(ChatHistory.RestoreItems(chat.Messages, chat.Model, vm.Services.Usage), null);
            vm.Page = "Chat"; Close();
        });
        var logs = new ListBox { ItemsSource = OrchDesk.Core.Telemetry.RunLog.Recent(Path.Combine(vm.Services.Store.Directory, "runs")), DisplayMemberPath = "FirstText", Height = 150 }; p.Children.Add(logs);
        var details = new TextBox { IsReadOnly = true, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, MinHeight = 200 }; p.Children.Add(details);
        logs.SelectionChanged += (_, _) => { if (logs.SelectedItem is OrchDesk.Core.Telemetry.RunLogSummary log) details.Text = string.Join("\n", OrchDesk.Core.Telemetry.RunLog.Read(log.Path).Select(e => e.At + " " + e.Role + " " + e.Text)); };
        return p;
    }
}
