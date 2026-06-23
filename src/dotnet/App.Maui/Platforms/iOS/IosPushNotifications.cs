using ActualChat.UI.Blazor;
using ActualChat.UI.Blazor.App;
using ActualChat.UI.Blazor.App.Services;
using ActualChat.UI.Blazor.Services;
using Plugin.Firebase.CloudMessaging;
using Plugin.Firebase.CloudMessaging.EventArgs;
using UserNotifications;

namespace ActualChat.App.Maui;

public class IosPushNotifications : UIServiceBase<AppUIHub>, IDeviceTokenRetriever, IDisposable
{
    private IFirebaseCloudMessaging Messaging { get; }

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

    // Silent dismissal pushes are handled in AppDelegate.DidReceiveRemoteNotification — iOS does
    // not route content-available background pushes to this Plugin.Firebase event.
    private void OnNotificationReceived(object? sender, FCMNotificationReceivedEventArgs e)
    { }

    private static void OnNotificationTapped(object? sender, FCMNotificationTappedEventArgs e)
    {
        if (!e.Notification.Data.TryGetValue(Constants.Notification.MessageDataKeys.Link, out var url))
            url = null;
        AppNavigationQueue.EnqueueOrNavigateToUrl(url, AutoNavigationReason.Notification);
    }
}
