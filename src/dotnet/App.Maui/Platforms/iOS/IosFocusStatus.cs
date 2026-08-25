using Intents;

namespace ActualChat.App.Maui;

// The iOS half of "does this phone want quiet". Only Focus is answerable - there is no public API
// for the Ring/Silent switch - so a phone muted by the switch alone still reads as unsilenced.
// Every probe fails open, like AndroidRingerMode: one bad read must not silently kill PTT.
public static class IosFocusStatus
{
    private static ILogger Log => field ??= StaticLog.For(typeof(IosFocusStatus));

    public static bool IsFocusActive {
        get {
            try {
                if (!OperatingSystem.IsIOSVersionAtLeast(15))
                    return false;

                var center = INFocusStatusCenter.DefaultCenter;
                if (center.AuthorizationStatus != INFocusStatusAuthorizationStatus.Authorized)
                    return false;

                // Null means the system won't say, which is not the same as "a Focus is on".
                return center.FocusStatus.IsFocused == true;
            }
            catch (Exception e) {
                Log.LogWarning(e, "Couldn't read the focus status");
                return false;
            }
        }
    }

    public static void EnsureAuthorized()
    {
        // Driven from IosPttUI rather than the wake path: the prompt needs a running app, and a
        // wake can arrive with the phone locked. Re-asking is free - the call only prompts while
        // the status is NotDetermined, and the user can revoke the grant in Settings at any time.
        try {
            if (!OperatingSystem.IsIOSVersionAtLeast(15))
                return;

            var center = INFocusStatusCenter.DefaultCenter;
            if (center.AuthorizationStatus != INFocusStatusAuthorizationStatus.NotDetermined)
                return;

            center.RequestAuthorization(
                status => Log.LogInformation("Focus status authorization: {Status}", status));
        }
        catch (Exception e) {
            Log.LogWarning(e, "Couldn't request the focus status authorization");
        }
    }
}
