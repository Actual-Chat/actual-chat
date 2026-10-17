using Intents;

namespace ActualChat.App.Maui;

// The Apple half of "does this device want quiet". Only Focus is answerable - there is no public API
// for the iPhone's Ring/Silent switch - so a phone muted by the switch alone still reads as unsilenced.
// Every probe fails open, like AndroidRingerMode: one bad read must not silently kill PTT.

public static class AppleFocusStatus
{
    private static ILogger Log => field ??= StaticLog.For(typeof(AppleFocusStatus));

    public static bool IsFocusActive {
        get {
            try {
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
        // Driven from a running, foreground app rather than the wake path: the prompt needs one, and
        // a wake can arrive with the phone locked. Re-asking is free - the call only prompts while
        // the status is NotDetermined, and the user can revoke the grant in Settings at any time.
        try {
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
