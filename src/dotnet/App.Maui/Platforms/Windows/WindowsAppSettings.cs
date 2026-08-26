using Windows.ApplicationModel;
using ActualChat.UI.Blazor.App.Services;
using ActualChat.Localization;
using Microsoft.Extensions.Localization;

namespace ActualChat.App.Maui;

public class WindowsAppSettings(IStringLocalizer l) : INativeAppSettings
{
    // ELEMENT_NOT_FOUND: StartupTask isn't registered (unpackaged or not declared in the manifest).
    private const int HResultElementNotFound = unchecked((int)0x80070490);

    public async Task<AutoStartState> GetAutoStartState()
    {
        var startupTask = await TryGetStartupTask().ConfigureAwait(true);
        if (startupTask == null)
            return new AutoStartState(false); // Auto-start not available in this install mode
        return startupTask.State switch {
            StartupTaskState.Enabled => new AutoStartState(true),
            StartupTaskState.EnabledByPolicy => new AutoStartState(true, false,
                l.NativeApp_AutoStartEnabledByPolicy),
            StartupTaskState.DisabledByPolicy => new AutoStartState(false, false,
                l.NativeApp_AutoStartDisabledByPolicy),
            StartupTaskState.DisabledByUser => new AutoStartState(false, false,
                l.NativeApp_AutoStartDisabledInTaskManager_Format(CoreConstants.AppName),
                OpenTaskManager),
            _ => new AutoStartState(false), // We assume it's disabled in any other case
        };
    }

    public async Task SetAutoStart(bool isEnabled)
    {
        var startupTask = await TryGetStartupTask().ConfigureAwait(true);
        if (startupTask == null)
            return; // Auto-start not available in this install mode
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

    private static async Task<StartupTask?> TryGetStartupTask()
    {
        try {
            // Pass the task ID you specified in the appxmanifest file
            return await StartupTask.GetAsync("{2720A628-2446-460A-9B15-9F3B41104E79}");
        }
        catch (COMException e) when (e.HResult == HResultElementNotFound) {
            // Unpackaged app or task not declared in the AppxManifest — auto-start is simply
            // not offered by the platform in this mode.
            return null;
        }
    }

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
