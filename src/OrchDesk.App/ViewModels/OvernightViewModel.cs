using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OrchDesk.App.Services;
using OrchDesk.Core.Overnight;
using OrchDesk.Core.Tools;

namespace OrchDesk.App.ViewModels;

/// <summary>Queue up work for the night, run it now or on a schedule, and read the morning report.</summary>
public sealed partial class OvernightViewModel : ObservableObject
{
    private readonly AppServices _services;
    private CancellationTokenSource? _cts;

    public ObservableCollection<OvernightTask> Tasks { get; } = [];
    public ObservableCollection<string> Log { get; } = [];
    public IReadOnlyList<OvernightMode> Modes { get; } = [OvernightMode.Auto, OvernightMode.Supervisor];

    [ObservableProperty, NotifyCanExecuteChangedFor(nameof(AddCommand))] private string _newTitle = "";
    [ObservableProperty, NotifyCanExecuteChangedFor(nameof(AddCommand))] private string _newPrompt = "";
    [ObservableProperty, NotifyCanExecuteChangedFor(nameof(AddCommand))] private string? _newWorkspace;
    [ObservableProperty] private string _newTestCommand = "dotnet test";
    [ObservableProperty] private OvernightMode _newMode = OvernightMode.Auto;
    [ObservableProperty] private OvernightTask? _selected;
    [ObservableProperty, NotifyCanExecuteChangedFor(nameof(RunNowCommand), nameof(StopCommand))] private bool _isRunning;
    [ObservableProperty] private string? _lastReportPath;
    [ObservableProperty] private string _scheduleText = "";

    public string StartTime
    {
        get => _services.Config.Overnight.StartTime;
        set { _services.Config.Overnight.StartTime = value; OnPropertyChanged(); }
    }

    public event Action<string>? Finished;

    public OvernightViewModel(AppServices services)
    {
        _services = services;
        NewWorkspace = services.Config.LastWorkspace;
        Reload();
        UpdateScheduleText();
    }

    private void Reload()
    {
        Tasks.Clear();
        foreach (var t in _services.Queue.Load()) Tasks.Add(t);
    }

    [RelayCommand]
    private void PickFolder()
    {
        var dlg = new Microsoft.Win32.OpenFolderDialog { Title = "Project folder for this task (must be a git repository)" };
        if (dlg.ShowDialog() == true) NewWorkspace = dlg.FolderName;
    }

    private bool CanAdd() => !string.IsNullOrWhiteSpace(NewPrompt) && !string.IsNullOrWhiteSpace(NewWorkspace);

    [RelayCommand(CanExecute = nameof(CanAdd))]
    private void Add()
    {
        var title = string.IsNullOrWhiteSpace(NewTitle) ? NewPrompt.Split('\n')[0].Trim() : NewTitle.Trim();
        if (title.Length > 70) title = title[..70] + "…";
        Tasks.Add(new OvernightTask
        {
            Title = title,
            Prompt = NewPrompt.Trim(),
            Workspace = NewWorkspace!,
            Mode = NewMode,
            TestCommand = string.IsNullOrWhiteSpace(NewTestCommand) ? null : NewTestCommand.Trim(),
        });
        _services.Queue.Save(Tasks);
        NewTitle = NewPrompt = "";
    }

    [RelayCommand]
    private void Remove(OvernightTask? task)
    {
        if (task is null || task.Status == OvernightStatus.Running) return;
        Tasks.Remove(task);
        _services.Queue.Save(Tasks);
    }

    [RelayCommand]
    private void Requeue(OvernightTask? task)
    {
        if (task is null || task.Status == OvernightStatus.Running) return;
        task.Status = OvernightStatus.Queued;
        task.Attempts = 0;
        task.Error = null;
        _services.Queue.Save(Tasks);
        Reload();
    }

    [RelayCommand]
    private void ClearFinished()
    {
        foreach (var t in Tasks.Where(t => t.Status is OvernightStatus.Succeeded or OvernightStatus.Cancelled).ToList()) Tasks.Remove(t);
        _services.Queue.Save(Tasks);
    }

    private bool CanRun() => !IsRunning;

    [RelayCommand(CanExecute = nameof(CanRun))]
    public async Task RunNowAsync()
    {
        _services.Queue.Save(Tasks);
        IsRunning = true;
        Log.Clear();
        _cts = new CancellationTokenSource();
        var runner = _services.CreateOvernightRunner();
        runner.Progress += line => Application.Current.Dispatcher.InvokeAsync(() =>
        {
            Log.Add(line);
            if (Log.Count > 500) Log.RemoveAt(0);
            Reload();
        });
        try
        {
            LastReportPath = await Task.Run(() => runner.RunAllAsync(_cts.Token));
            Log.Add($"Morning report written: {LastReportPath}");
            Finished?.Invoke(LastReportPath);
        }
        catch (OperationCanceledException) { Log.Add("Stopped. Unfinished tasks stay in the queue."); }
        catch (Exception ex) { Log.Add("Runner error: " + ex.Message); }
        finally
        {
            IsRunning = false;
            _cts.Dispose();
            _cts = null;
            Reload();
        }
    }

    [RelayCommand(CanExecute = nameof(IsRunning))]
    private void Stop() => _cts?.Cancel();

    [RelayCommand]
    private void OpenReport()
    {
        var path = LastReportPath ?? Directory.EnumerateFiles(_services.ReportsDir, "overnight-*.md").OrderDescending().FirstOrDefault();
        if (path is null) { Log.Add("No morning report yet."); return; }
        Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
    }

    /// <summary>Registers a Windows scheduled task that wakes the PC and runs "OrchDesk.exe --overnight" nightly.</summary>
    [RelayCommand]
    private async Task ScheduleNightlyAsync()
    {
        if (!TimeOnly.TryParse(StartTime, out var time)) { ScheduleText = "Start time must look like 01:00."; return; }
        _services.Store.Save(_services.Config);
        var exe = Environment.ProcessPath ?? Path.Combine(AppContext.BaseDirectory, "OrchDesk.exe");
        var xmlPath = Path.Combine(_services.Store.Directory, "overnight-task.xml");
        await File.WriteAllTextAsync(xmlPath, OvernightSchedule.BuildTaskXml(exe, time), System.Text.Encoding.Unicode);
        var r = await ProcessRunner.RunAsync(
            ProcessRunner.Exe("schtasks", ["/Create", "/TN", OvernightSchedule.TaskName, "/XML", xmlPath, "/F"], _services.Store.Directory),
            TimeSpan.FromSeconds(30), CancellationToken.None);
        ScheduleText = r.ExitCode == 0 ? $"Runs every night at {time:HH:mm}, waking the PC if it's asleep." : "Couldn't create the scheduled task: " + r.Output.Trim();
    }

    [RelayCommand]
    private async Task UnscheduleAsync()
    {
        var r = await ProcessRunner.RunAsync(
            ProcessRunner.Exe("schtasks", ["/Delete", "/TN", OvernightSchedule.TaskName, "/F"], _services.Store.Directory),
            TimeSpan.FromSeconds(30), CancellationToken.None);
        ScheduleText = r.ExitCode == 0 ? "Nightly run removed." : r.Output.Trim();
    }

    private async void UpdateScheduleText()
    {
        if (!OperatingSystem.IsWindows()) return;
        var r = await ProcessRunner.RunAsync(
            ProcessRunner.Exe("schtasks", ["/Query", "/TN", OvernightSchedule.TaskName], _services.Store.Directory),
            TimeSpan.FromSeconds(15), CancellationToken.None);
        ScheduleText = r.ExitCode == 0 ? "A nightly run is scheduled." : "Not scheduled. Runs only when you press Run queue now.";
    }
}
