using ActualChat.App.Maui.Services;
using Foundation;
using UIKit;
using UserNotifications;
using DeviceType = ActualChat.Notifications.DeviceType;
using ILogger = Microsoft.Extensions.Logging.ILogger;

namespace ActualChat.App.Maui;

/// <summary>
/// FCM token forwarding and silent-dismissal push handling shared by the iOS and
/// MacCatalyst <c>AppDelegate</c>s (both use the AdamE native Firebase binding).
/// </summary>
public static class AppleNotifications
{
    public static void ForwardRegistrationToken(string fcmToken, DeviceType deviceType, ILogger log)
    {
        log.LogDebug("OnNewToken: '{Token}'", fcmToken);
        var mauiNotifications = IPlatformApplication.Current?.Services.GetService<MauiNotifications>();
        if (mauiNotifications != null)
            _ = BackgroundTask.Run(
                () => mauiNotifications.RefreshNotificationToken(fcmToken, deviceType, CancellationToken.None),
                log, "DidReceiveRegistrationToken failed");
    }

    public static UIBackgroundFetchResult HandleRemoteNotification(NSDictionary userInfo, ILogger log)
    {
        try {
            var dismissedTagsKey = new NSString(Constants.Notification.MessageDataKeys.DismissedTags);
            var dismissedTags = (userInfo[dismissedTagsKey] as NSString)?.ToString();
            if (!dismissedTags.IsNullOrEmpty()) {
                var tags = dismissedTags.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                RemoveDeliveredNotifications(tags);
                return UIBackgroundFetchResult.NewData;
            }
        }
        catch (Exception e) {
            log.LogError(e, "DidReceiveRemoteNotification failed");
        }
        return UIBackgroundFetchResult.NoData;
    }

    private static void RemoveDeliveredNotifications(IReadOnlyCollection<string> dismissedTags)
        => UNUserNotificationCenter.Current.GetDeliveredNotifications(delivered => {
            var toRemove = delivered
                .Where(n => dismissedTags.Contains(n.Request.Content.ThreadIdentifier))
                .Select(n => n.Request.Identifier)
                .ToArray();
            if (toRemove.Length > 0)
                UNUserNotificationCenter.Current.RemoveDeliveredNotifications(toRemove);
        });
}
