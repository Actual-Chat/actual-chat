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

    public Task<PermissionState> GetNotificationPermissionState(CancellationToken cancellationToken)
    {
        var activity = Platform.CurrentActivity!;
        if (ContextCompat.CheckSelfPermission(activity, Manifest.Permission.PostNotifications) == Permission.Granted)
            return Task.FromResult(PermissionState.Granted);
        return Task.FromResult(activity.ShouldShowRequestPermissionRationale(Manifest.Permission.PostNotifications)
            ? PermissionState.Denied
            : PermissionState.Prompt);
    }

    public async Task RequestNotificationPermissions(CancellationToken cancellationToken)
    {
        var activity = Platform.CurrentActivity!;
        var state = await GetNotificationPermissionState(cancellationToken);

        if (state == PermissionState.Granted)
            return;

        if (state == PermissionState.Denied) {
            new AlertDialog.Builder(activity)
                .SetTitle("Enable app permissions")!
                .SetMessage("Grant notifications permission to receive push notifications for new messages")!
                .SetNegativeButton("No thanks", (_, _) => { })!
                .SetPositiveButton("Continue",
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
            activity.RequestPermissions( Manifest.Permission.PostNotifications);
        // Code below doesn't work
        // ActivityCompat.RequestPermissions(activity, new[] { Manifest.Permission.PostNotifications }, MainActivity.NotificationPermissionID);
    }

}
