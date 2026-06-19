using ActualChat.UI.Blazor.App;
using ActualChat.UI;
using ActualChat.UI.Blazor;
using ActualChat.UI.Blazor.App.Services;
using ActualChat.UI.Blazor.Services;
using Plugin.Firebase.CloudMessaging;
using Plugin.Firebase.CloudMessaging.EventArgs;
using UIKit;
using UserNotifications;

namespace ActualChat.App.Maui;

public class IosPushNotifications : UIServiceBase<AppUIHub>, IDeviceTokenRetriever, INotificationsPermission, IDisposable
{
    private IFirebaseCloudMessaging Messaging { get; }

    private NotificationUI NotificationUI => field ??= Hub.Services.GetRequiredService<NotificationUI>();
    private SystemSettingsUI SystemSettingsUI => field ??= Hub.Services.GetRequiredService<SystemSettingsUI>();
    private static UNUserNotificationCenter NotificationCenter => UNUserNotificationCenter.Current;

    public IosPushNotifications(AppUIHub hub) : base(hub)
    {
        Messaging = hub.Services.GetRequiredService<IFirebaseCloudMessaging>();
        Messaging.NotificationTapped += OnNotificationTapped;
        Messaging.NotificationReceived += OnNotificationReceived;
    }

    public void Dispose()
        => Messaging.NotificationTapped -= OnNotificationTapped;

    public Task<string?> GetDeviceToken(CancellationToken cancellationToken)
        => Messaging.GetTokenAsync();

    // TODO(AK): it's suspicious that we can't remove FCM token there - no API available
    public Task DeleteDeviceToken(CancellationToken cancellationToken)
        => Task.CompletedTask;

    public async Task<bool?> IsGranted(CancellationToken cancellationToken = default)
    {
        var settings = await NotificationCenter.GetNotificationSettingsAsync().ConfigureAwait(false);
        return settings.AuthorizationStatus switch {
            UNAuthorizationStatus.NotDetermined => null,
            UNAuthorizationStatus.Authorized => true,
            UNAuthorizationStatus.Provisional => true,
            UNAuthorizationStatus.Ephemeral => true,
            _ => false,
        };
    }

    public Task Request(CancellationToken cancellationToken = default)
        => ForegroundTask.Run(async () => {
            var isGranted = await IsGranted(cancellationToken).ConfigureAwait(true);
            if (isGranted == true) {
                NotificationUI.SetIsGranted(isGranted);
                return;
            }

            // TODO: replace with Messaging.CheckIfValidAsync()?
            if (!UIDevice.CurrentDevice.CheckSystemVersion(10, 0))
                return;

            // For iOS 10 display notification (sent via APNS)
            var options = UNAuthorizationOptions.Alert
                | UNAuthorizationOptions.Badge
                | UNAuthorizationOptions.Sound;
            var (result, error) = await NotificationCenter.RequestAuthorizationAsync(options).ConfigureAwait(true);
            if (result)
                Log.LogInformation("NotificationCenter.RequestAuthorizationAsync: granted");
            else
                Log.LogWarning("NotificationCenter.RequestAuthorizationAsync: denied, {Error}", error);

            isGranted = await IsGranted(cancellationToken).ConfigureAwait(true);
            if (isGranted == false)
                await SystemSettingsUI.Open().ConfigureAwait(true);
            NotificationUI.SetIsGranted(isGranted);
        }, Log, "Notifications permission request failed", cancellationToken);

    private void OnNotificationReceived(object? sender, FCMNotificationReceivedEventArgs e)
    {
        if (!e.Notification.Data.TryGetValue(Constants.Notification.MessageDataKeys.DismissedTags, out var dismissedTagsRaw)
            || dismissedTagsRaw.IsNullOrEmpty())
            return;

        var dismissedTags = dismissedTagsRaw.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        NotificationCenter.GetDeliveredNotifications(delivered => {
            var toRemove = delivered
                .Where(n => dismissedTags.Contains(n.Request.Content.ThreadIdentifier))
                .Select(n => n.Request.Identifier)
                .ToArray();
            if (toRemove.Length > 0)
                NotificationCenter.RemoveDeliveredNotifications(toRemove);
        });
    }

    private static void OnNotificationTapped(object? sender, FCMNotificationTappedEventArgs e)
    {
        if (!e.Notification.Data.TryGetValue(Constants.Notification.MessageDataKeys.Link, out var url))
            url = null;
        AppNavigationQueue.EnqueueOrNavigateToUrl(url, AutoNavigationReason.Notification);
    }
}
