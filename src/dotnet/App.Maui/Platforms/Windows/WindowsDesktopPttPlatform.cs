using ActualChat.UI.Blazor.App.Services;
using Windows.UI.Shell;

namespace ActualChat.App.Maui;

// Windows has no public API for Do Not Disturb itself, so IsQuiet combines three probes: the shell's
// quiet-hours state (what the Do Not Disturb toggle and its automatic rules flip), a Focus session
// started from the Clock app, and presentation mode.

public sealed partial class WindowsDesktopPttPlatform : DesktopPttPlatform
{
    // WNF_SHEL_QUIETHOURS_ACTIVE_PROFILE_CHANGED: 0 is off, anything else names a Do Not Disturb
    // profile. Undocumented, but unchanged since Windows 10 1803, and only ever read here.
    private const ulong QuietHoursStateName = 0x0D83063EA3BF1C75;
    private const int QunsPresentationMode = 4;
    private const int QunsQuietTime = 6;
    private static readonly ILogger Log = StaticLog.For<WindowsDesktopPttPlatform>();

    public override bool IsQuiet => IsDoNotDisturbOn() || IsFocusSessionActive() || IsPresenting();

    // Private methods

    private static bool IsDoNotDisturbOn()
    {
        try {
            var stateName = QuietHoursStateName;
            var bufferSize = (uint)sizeof(uint);
            var status = NtQueryWnfStateData(ref stateName, 0, 0, out _, out var profile, ref bufferSize);
            return status == 0 && profile != 0;
        }
        catch (Exception e) {
            Log.LogWarning(e, "Couldn't read the Do Not Disturb state");
            return false;
        }
    }

    private static bool IsFocusSessionActive()
    {
        try {
            // FocusSessionManager arrived in Windows 11 22H2; the app still runs on Windows 10
            if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22621) || !FocusSessionManager.IsSupported)
                return false;

            return FocusSessionManager.GetDefault().IsFocusActive;
        }
        catch (Exception e) {
            Log.LogWarning(e, "Couldn't read the Focus session state");
            return false;
        }
    }

    private static bool IsPresenting()
    {
        try {
            return SHQueryUserNotificationState(out var state) == 0
                && state is QunsPresentationMode or QunsQuietTime;
        }
        catch (Exception e) {
            Log.LogWarning(e, "Couldn't read the user notification state");
            return false;
        }
    }

    [LibraryImport("ntdll.dll")]
    private static partial int NtQueryWnfStateData(
        ref ulong stateName, nint typeId, nint explicitScope,
        out uint changeStamp, out uint buffer, ref uint bufferSize);

    [LibraryImport("shell32.dll")]
    private static partial int SHQueryUserNotificationState(out int state);
}
