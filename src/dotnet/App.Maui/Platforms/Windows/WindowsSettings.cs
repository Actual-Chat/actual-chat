using ActualChat.Chat.UI.Blazor.Components;
using Windows.ApplicationModel;

namespace ActualChat.App.Maui;

public class WindowsSettings : IWindowsSettings
{
    public async Task<AutoStartupState> GetAutoStartupState()
    {
        var startupTask = await GetStartupTask().ConfigureAwait(true);
        switch (startupTask.State) {
        case StartupTaskState.Disabled:
            return new AutoStartupState(false);
        case StartupTaskState.DisabledByUser: {
            var reason1 = "You have disabled this app's ability to run "
                + "as soon as you sign in, but if you change your mind, "
                + "you can enable this in the Startup tab in Task Manager.";
            return new AutoStartupState(false, false, reason1, OpenTaskManager);
        }
        case StartupTaskState.DisabledByPolicy:
            var reason2 = "Startup disabled by group policy, or not supported on this device.";
            return new AutoStartupState(false, false, reason2);
        case StartupTaskState.Enabled:
            return new AutoStartupState(true);
        case StartupTaskState.EnabledByPolicy:
            var reason3 = "Startup enabled by group policy.";
            return new AutoStartupState(true, false, reason3);
        default:
            throw new ArgumentOutOfRangeException();
        }
    }

    public async Task SetAutoStartup(bool isEnabled)
    {
        var startupTask = await GetStartupTask().ConfigureAwait(true);
        if (isEnabled) {
            switch (startupTask.State) {
            case StartupTaskState.Enabled:
            case StartupTaskState.EnabledByPolicy:
                break;
            case StartupTaskState.Disabled:
                // Task is disabled but can be enabled.
                // Ensure that you are on a UI thread when you call RequestEnableAsync()
                var newState = await startupTask
                    .RequestEnableAsync();
                Debug.WriteLine("Request to enable startup, result = {0}", newState);
                break;
            case StartupTaskState.DisabledByUser:
            case StartupTaskState.DisabledByPolicy:
                throw new InvalidOperationException();
            default:
                throw new ArgumentOutOfRangeException();
            }
        }
        else {
            switch (startupTask.State) {
            case StartupTaskState.Disabled:
            case StartupTaskState.DisabledByUser:
            case StartupTaskState.DisabledByPolicy:
                break;
            case StartupTaskState.Enabled:
                startupTask.Disable();
                break;
            case StartupTaskState.EnabledByPolicy:
                throw new InvalidOperationException();
            default:
                throw new ArgumentOutOfRangeException();
            }
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
            Arguments = "/7 /startup" // special arguments to open startup page
        };
        Process.Start(processStartInfo);
    }
}
