using System.Runtime.InteropServices;

namespace OrchDesk.Core.Overnight;

/// <summary>
/// Stops Windows from going to sleep while the overnight queue runs (it can't stop a lid close or the power
/// button). Dispose to let the PC sleep again. No-op on other operating systems.
/// </summary>
public sealed partial class KeepAwake : IDisposable
{
    private const uint EsContinuous = 0x80000000;
    private const uint EsSystemRequired = 0x00000001;
    private readonly bool _active;

    public KeepAwake()
    {
        if (!OperatingSystem.IsWindows()) return;
        _active = SetThreadExecutionState(EsContinuous | EsSystemRequired) != 0;
    }

    public void Dispose()
    {
        if (_active) SetThreadExecutionState(EsContinuous);
    }

    [LibraryImport("kernel32.dll")]
    private static partial uint SetThreadExecutionState(uint esFlags);
}

/// <summary>
/// Builds a Windows Task Scheduler definition that wakes the PC and starts "OrchDesk.exe --overnight" each night.
/// XML is used because WakeToRun can't be set with plain schtasks flags. Register with:
///   schtasks /Create /TN "OrchDesk\Overnight" /XML &lt;file&gt; /F
/// </summary>
public static class OvernightSchedule
{
    public const string TaskName = @"OrchDesk\Overnight";

    public static string BuildTaskXml(string exePath, TimeOnly startTime)
    {
        var start = DateTime.Today.Add(startTime.ToTimeSpan()).ToString("yyyy-MM-ddTHH:mm:ss");
        var exe = System.Security.SecurityElement.Escape(exePath);
        return $"""
            <?xml version="1.0" encoding="UTF-16"?>
            <Task version="1.2" xmlns="http://schemas.microsoft.com/windows/2004/02/mit/task">
              <RegistrationInfo><Description>Runs the OrchDesk overnight task queue.</Description></RegistrationInfo>
              <Triggers>
                <CalendarTrigger>
                  <StartBoundary>{start}</StartBoundary>
                  <Enabled>true</Enabled>
                  <ScheduleByDay><DaysInterval>1</DaysInterval></ScheduleByDay>
                </CalendarTrigger>
              </Triggers>
              <Principals><Principal id="Author"><LogonType>InteractiveToken</LogonType><RunLevel>LeastPrivilege</RunLevel></Principal></Principals>
              <Settings>
                <MultipleInstancesPolicy>IgnoreNew</MultipleInstancesPolicy>
                <DisallowStartIfOnBatteries>false</DisallowStartIfOnBatteries>
                <StopIfGoingOnBatteries>false</StopIfGoingOnBatteries>
                <StartWhenAvailable>true</StartWhenAvailable>
                <WakeToRun>true</WakeToRun>
                <ExecutionTimeLimit>PT10H</ExecutionTimeLimit>
                <Enabled>true</Enabled>
              </Settings>
              <Actions Context="Author"><Exec><Command>"{exe}"</Command><Arguments>--overnight</Arguments></Exec></Actions>
            </Task>
            """;
    }
}
