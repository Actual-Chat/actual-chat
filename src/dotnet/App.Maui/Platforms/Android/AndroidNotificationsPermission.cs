using ActualChat.Notification.UI.Blazor;
using ActualChat.UI;
using ActualChat.UI.Blazor;
using Android;
using Android.App;
using Android.Content.PM;
using AndroidX.Core.Content;

namespace ActualChat.App.Maui;

public class AndroidNotificationsPermission(UIHub hub) : INotificationsPermission
{
    private NotificationUI? _notificationUI;
    private SystemSettingsUI? _systemSettingsUI;
    private ILogger? _log;

    private NotificationUI NotificationUI => _notificationUI ??= hub.GetRequiredService<NotificationUI>();
    private SystemSettingsUI SystemSettingsUI => _systemSettingsUI ??= hub.GetRequiredService<SystemSettingsUI>();
    private ILogger Log => _log ??= hub.LogFor(GetType());

    public Task<bool?> IsGranted(CancellationToken cancellationToken = default)
    {
        var activity = Platform.CurrentActivity;
        var permission = activity != null
            ? ContextCompat.CheckSelfPermission(activity, Manifest.Permission.PostNotifications)
            : Permission.Denied;
        return Task.FromResult((bool?)(permission == Permission.Granted));
    }

    public Task Request(CancellationToken cancellationToken = default)
        => ForegroundTask.Run(async () => {
            var isGranted = await IsGranted(cancellationToken).ConfigureAwait(true);
            if (isGranted == true) {
                NotificationUI.SetIsGranted(isGranted);
                return;
            }

            if (Platform.CurrentActivity is not MainActivity activity)
                return;

            var whenCompletedSource = TaskCompletionSourceExt.New();
            _ = Task.Delay(MainActivity.MaxPermissionRequestDuration, cancellationToken)
                .ContinueWith(_ => whenCompletedSource.TrySetResult(), TaskScheduler.Default);
            if (!activity.ShouldShowRequestPermissionRationale(Manifest.Permission.PostNotifications))
                RequestPermission();
            else
                new AlertDialog.Builder(activity)
                    .SetTitle("Notifications permission isn't granted")!
                    .SetMessage("""
                                Actual Chat can notify you about new content in chats, friend requests, and other activities related to your account.

                                Do you want to allow Actual Chat sending notifications to this device?
                                """)!
                    .SetNegativeButton("Decline", (_, _) => whenCompletedSource.TrySetResult())!
                    .SetPositiveButton("Allow", (_, _) => RequestPermission())!
                    .Show();
            await whenCompletedSource.Task.ConfigureAwait(false);
            isGranted = await IsGranted(cancellationToken).ConfigureAwait(false);
            if (isGranted == false)
                await SystemSettingsUI.Open().ConfigureAwait(false);
            NotificationUI.SetIsGranted(isGranted);

            void RequestPermission()
                => activity.RequestPermission(Manifest.Permission.PostNotifications, whenCompletedSource);
        }, Log, "Notifications permission request failed", cancellationToken);

}
