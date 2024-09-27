using ActualChat.UI.Blazor.App;
using ActualChat.UI;
using ActualChat.UI.Blazor;
using ActualChat.UI.Blazor.Services;
using Foundation;
using Microsoft.AspNetCore.Components;
using Plugin.Firebase.CloudMessaging;
using Plugin.Firebase.CloudMessaging.EventArgs;
using Serilog;
using UIKit;
using UserNotifications;
using ILogger = Microsoft.Extensions.Logging.ILogger;

namespace ActualChat.App.Maui;

public class IosPushNotifications : IDeviceTokenRetriever, INotificationsPermission, IDisposable
{
    private NotificationUI? _notificationUI;
    private SystemSettingsUI? _systemSettingsUI;
    private ILogger? _log;

    private UIHub Hub { get; }
    private IFirebaseCloudMessaging Messaging { get; }
    private UrlMapper UrlMapper => Hub.UrlMapper();
    private NavigationManager Nav => Hub.Nav;
    private NotificationUI NotificationUI => _notificationUI ??= Hub.GetRequiredService<NotificationUI>();
    private SystemSettingsUI SystemSettingsUI => _systemSettingsUI ??= Hub.GetRequiredService<SystemSettingsUI>();
    private static UNUserNotificationCenter NotificationCenter => UNUserNotificationCenter.Current;
    private ILogger Log => _log ??= Hub.LogFor(GetType());

    public IosPushNotifications(UIHub hub)
    {
        Hub = hub;
        Messaging = hub.GetRequiredService<IFirebaseCloudMessaging>();
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
        => _ = DispatchToBlazor(_ => {
            var unreadChatCount = Hub.ChatUIHub().ChatListUI.UnreadChatCount.Value.Value;
            UNUserNotificationCenter.Current.SetBadgeCount(unreadChatCount, null);
        }, "PushNotifications.OnNotificationReceived()");

    private static void OnNotificationTapped(object? sender, FCMNotificationTappedEventArgs e)
    {
        if (!e.Notification.Data.TryGetValue(Constants.Notification.MessageDataKeys.Link, out var url))
            url = null;
        AppNavigationQueue.EnqueueOrNavigateToNotificationUrl(url);
    }
}
