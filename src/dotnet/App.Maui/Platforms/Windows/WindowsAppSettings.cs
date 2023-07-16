using Windows.ApplicationModel;
using ActualChat.Chat.UI.Blazor.Services;

namespace ActualChat.App.Maui;

public class WindowsAppSettings : INativeAppSettings
{
    public async Task<AutoStartState> GetAutoStartState()
    {
        var startupTask = await GetStartupTask().ConfigureAwait(true);
        return startupTask.State switch {
            StartupTaskState.Enabled => new AutoStartState(true),
            StartupTaskState.EnabledByPolicy => new AutoStartState(true, false,
                "Auto-start is enabled by group/machine policy."),
            StartupTaskState.DisabledByPolicy => new AutoStartState(false, false,
                "Auto-start is disabled by group/machine policy or is not supported on this device."),
            StartupTaskState.DisabledByUser => new AutoStartState(false, false,
                "Actual Chat auto-start is disabled in Task Manager. "
                + "You can re-enable it in its Startup tab.",
                OpenTaskManager),
            _ => new AutoStartState(false), // We assume it's disabled in any other case
        };
    }

    public async Task SetAutoStart(bool isEnabled)
    {
        var startupTask = await GetStartupTask().ConfigureAwait(true);
        if (isEnabled) {
            switch (startupTask.State) {
            case StartupTaskState.Enabled:
            case StartupTaskState.EnabledByPolicy:
                break; // Already enabled
            case StartupTaskState.Disabled:
                // Task is disabled but can be enabled.
                // Ensure that you are on a UI thread when you call RequestEnableAsync()
                var newState = await startupTask.RequestEnableAsync();
                Debug.WriteLine("Request to enable auto-start, result = {0}", newState);
                break;
            }
            // We can't do anything otherwise
        }
        else {
            switch (startupTask.State) {
            case StartupTaskState.Disabled:
            case StartupTaskState.DisabledByUser:
            case StartupTaskState.DisabledByPolicy:
                break; // Already disabled
            case StartupTaskState.Enabled:
                startupTask.Disable();
                break;
            }
            // We can't do anything otherwise
        }
    }

    private static async Task<StartupTask> GetStartupTask()
        // Pass the task ID you specified in the appxmanifest file
        => await StartupTask.GetAsync("{2720A628-2446-460A-9B15-9F3B41104E79}");

    private void OpenTaskManager()
    {
        var processStartInfo = new ProcessStartInfo {
            FileName = "taskmgr.exe",
            UseShellExecute = true,
            Verb = "runas",
            Arguments = "/7 /startup", // Special arguments to open Startup tab
        };
        Process.Start(processStartInfo);
    }
}
