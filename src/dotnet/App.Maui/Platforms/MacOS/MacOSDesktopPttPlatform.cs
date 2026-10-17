using ActualChat.UI.Blazor.App.Services;
using Foundation;

namespace ActualChat.App.Maui;

// A Focus covers Do Not Disturb on macOS. Reading it takes the Communication Notifications
// capability; until the app is granted it, AppleFocusStatus reads "not focused" and PTT keeps playing.

public sealed class MacOSDesktopPttPlatform : DesktopPttPlatform
{
    private readonly Lock _lock = new();
    private NSObject? _activityToken;
    private bool _isAuthorizationRequested;

    public override bool IsQuiet {
        get {
            // Asked only once a chat is armed - the earliest point the prompt makes sense to the user
            if (!_isAuthorizationRequested) {
                _isAuthorizationRequested = true;
                BeginDispatchToMainThread(AppleFocusStatus.EnsureAuthorized);
            }
            return AppleFocusStatus.IsFocusActive;
        }
    }

    public override void OnIsArmedChanged(bool isArmed)
    {
        // App Nap throttles a hidden app's timers and I/O, which delays the voice an armed chat is
        // waiting for; the activity opts out of it, yet still lets the Mac sleep.
        lock (_lock) {
            if (isArmed && _activityToken is null)
                _activityToken = NSProcessInfo.ProcessInfo.BeginActivity(
                    NSActivityOptions.InitiatedAllowingIdleSystemSleep, "Push-to-talk is armed");
            else if (!isArmed && _activityToken is { } activityToken) {
                NSProcessInfo.ProcessInfo.EndActivity(activityToken);
                _activityToken = null;
            }
        }
    }
}
