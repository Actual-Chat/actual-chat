using ActualChat.Notification.UI.Blazor;
using Android;
using Android.App;
using Android.Content.PM;
using AndroidX.Core.Content;

namespace ActualChat.App.Maui;

public class AndroidNotificationPermissions : INotificationPermissions
{
    public AndroidNotificationPermissions()
    { }

    public Task<PermissionState> GetPermissionState(CancellationToken cancellationToken)
    {
        var activity = Platform.CurrentActivity!;
        if (ContextCompat.CheckSelfPermission(activity, Manifest.Permission.PostNotifications) == Permission.Granted)
            return Task.FromResult(PermissionState.Granted);
        return Task.FromResult(activity.ShouldShowRequestPermissionRationale(Manifest.Permission.PostNotifications)
            ? PermissionState.Denied
            : PermissionState.Prompt);
    }

    public async Task RequestNotificationPermission(CancellationToken cancellationToken)
    {
        var activity = Platform.CurrentActivity!;
        var state = await GetPermissionState(cancellationToken).ConfigureAwait(true);
        if (state == PermissionState.Granted)
            return;

        if (state == PermissionState.Denied) {
            new AlertDialog.Builder(activity)
                .SetTitle("Notification permission isn't granted")!
                .SetMessage("""
                    Actual Chat can send notifications about the new content in chats, new friend requests, and other activities related to your account.

                    Do you want to allow Actual Chat sending notifications to this device?
                    """)!
                .SetNegativeButton("Decline", (_, _) => { })!
                .SetPositiveButton("Allow",
                    (_, _) =>  RequestPermission())!
                .Show();
            return;
        }
        // else
        RequestPermission();
    }

    private void RequestPermission()
    {
        if (Platform.CurrentActivity is MainActivity activity)
            activity.RequestPermissions(Manifest.Permission.PostNotifications);
    }

}
