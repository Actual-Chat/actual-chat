using ActualChat.Localization;
using ActualChat.UI;
using ActualChat.UI.Blazor;
using ActualChat.UI.Blazor.App;
using ActualChat.UI.Blazor.App.Services;
using Android;
using Android.App;
using Android.Content.PM;
using AndroidX.Core.Content;

namespace ActualChat.App.Maui;

public class AndroidNotificationsPermission(AppUIHub hub) : UIServiceBase<AppUIHub>(hub), INotificationsPermission
{
    private NotificationUI NotificationUI => Hub.NotificationUI;
    private SystemSettingsUI SystemSettingsUI => field ??= Hub.Services.GetRequiredService<SystemSettingsUI>();

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

            var whenCompletedSource = TaskCompletionSourceExt.New<bool>();
            _ = Task.Delay(MainActivity.MaxPermissionRequestDuration, cancellationToken)
                .ContinueWith(_ => whenCompletedSource.TrySetResult(false), TaskScheduler.Default);
            if (!activity.ShouldShowRequestPermissionRationale(Manifest.Permission.PostNotifications))
                RequestPermission();
            else
                new AlertDialog.Builder(activity)
                    .SetTitle(L.NotificationsPermission_Title)!
                    .SetMessage(L.NotificationsPermission_Message_Format(CoreConstants.AppName))!
                    .SetNegativeButton(L.Common_Decline, (_, _) => whenCompletedSource.TrySetResult(false))!
                    .SetPositiveButton(L.Common_Allow, (_, _) => RequestPermission())!
                    .Show();
            await whenCompletedSource.Task.ConfigureAwait(false);
            isGranted = await IsGranted(cancellationToken).ConfigureAwait(false);
            if (isGranted == false)
                await SystemSettingsUI.Open().ConfigureAwait(false);
            NotificationUI.SetIsGranted(isGranted);

            void RequestPermission()
                => activity.RequestPermission(Manifest.Permission.PostNotifications, c => whenCompletedSource.TrySetResult(c));
        }, Log, "Notifications permission request failed", cancellationToken);

}
