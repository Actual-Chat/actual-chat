using ActualChat.App.Maui.Services;
using Firebase.CloudMessaging;
using Foundation;
using UIKit;
using UserNotifications;
using DeviceType = ActualChat.Notifications.DeviceType;
using ILogger = Microsoft.Extensions.Logging.ILogger;
using NotificationExt = ActualChat.Notifications.NotificationExt;

namespace ActualChat.App.Maui;

[Register("AppDelegate")]
public class AppDelegate : MauiUIApplicationDelegate, IMessagingDelegate
{
    private static ILogger? _log;
    private static ILogger Log => _log ??= StaticLog.Factory.CreateLogger<AppDelegate>();

    protected override MauiApp CreateMauiApp()
    {
        NSHttpCookieStorage.SharedStorage.AcceptPolicy = NSHttpCookieAcceptPolicy.Always;
        return MauiProgram.CreateMauiApp();
    }

    // Silent dismissal pushes (content-available, no alert) are delivered here, not to the
    // Plugin.Firebase NotificationReceived event — that only fires for foreground alert pushes.
    // The badge does not ride this one - a background aps may carry only content-available, and
    // iOS ignores a badge next to it - so SendBadge sends the count as its own alert push.
    [Export("application:didReceiveRemoteNotification:fetchCompletionHandler:")]
    public void DidReceiveRemoteNotification(
        UIApplication application, NSDictionary userInfo, Action<UIBackgroundFetchResult> completionHandler)
    {
        try {
            var dismissedIds = GetValue(userInfo, Constants.Notification.MessageDataKeys.DismissedIds);
            var dismissedTags = GetValue(userInfo, Constants.Notification.MessageDataKeys.DismissedTags);
            if (!dismissedIds.IsNullOrEmpty() || !dismissedTags.IsNullOrEmpty()) {
                var tags = Split(dismissedTags);
                if (IosCallKit.IsAvailable)
                    EndDismissedCalls(tags);
                RemoveDeliveredNotifications(Split(dismissedIds), tags);
                completionHandler(UIBackgroundFetchResult.NewData);
                return;
            }
        }
        catch (Exception e) {
            Log.LogError(e, "DidReceiveRemoteNotification failed");
        }

        completionHandler(UIBackgroundFetchResult.NoData);
    }

    [Export ("messaging:didReceiveRegistrationToken:")]
    public void DidReceiveRegistrationToken (Firebase.CloudMessaging.Messaging messaging, string fcmToken)
    {
        // Monitor token generation: To be notified whenever the token is updated.
        var token = fcmToken;
        Log.LogDebug("OnNewToken: '{Token}'", token);
        var appServices = IPlatformApplication.Current?.Services;
        var mauiNotifications = appServices?.GetService<MauiNotifications>();
        if (mauiNotifications != null )
            _ = BackgroundTask.Run(
                () => mauiNotifications.RefreshNotificationToken(token, DeviceType.iOSApp, CancellationToken.None),
                Log, "DidReceiveRegistrationToken failed");
    }

    // Private methods

    private static void EndDismissedCalls(IReadOnlyCollection<string> dismissedTags)
    {
        // An independent path to the reactive LiveSessionUI one: the server's cancel dismissal
        // arrives even when the reactive state is slow. Ringing calls only - the accept the user
        // just made is dismissed exactly the same way, and ending that one kills a live call.
        foreach (var tag in dismissedTags) {
            if (NotificationExt.TryParseCallTag(tag) is { } callId)
                IosCalls.Instance.EndRingingCall(callId.ChatId, callId);
        }
    }

    private static void RemoveDeliveredNotifications(
        IReadOnlyCollection<string> dismissedIds,
        IReadOnlyCollection<string> dismissedTags)
        => UNUserNotificationCenter.Current.GetDeliveredNotifications(delivered => {
            var toRemove = delivered
                .Where(n => IsDismissed(n, dismissedIds, dismissedTags))
                .Select(n => n.Request.Identifier)
                .ToArray();
            if (toRemove.Length > 0)
                UNUserNotificationCenter.Current.RemoveDeliveredNotifications(toRemove);
        });

    private static bool IsDismissed(
        UNNotification notification,
        IReadOnlyCollection<string> dismissedIds,
        IReadOnlyCollection<string> dismissedTags)
    {
        // The id identifies the banner exactly. The thread id is the whole chat, so matching on it
        // takes that chat's live mentions down with an ordinary message dismissal, and never equals
        // the entry-derived tag a mention or reaction is dismissed by - it is the fallback only for
        // a banner delivered without an id.
        var notificationId = GetValue(notification.Request.Content.UserInfo,
            Constants.Notification.MessageDataKeys.NotificationId);
        return notificationId.IsNullOrEmpty()
            ? dismissedTags.Contains(notification.Request.Content.ThreadIdentifier)
            : dismissedIds.Contains(notificationId);
    }

    private static string GetValue(NSDictionary userInfo, string key)
    {
        var nsKey = new NSString(key);
        return userInfo.ContainsKey(nsKey) ? (userInfo[nsKey] as NSString)?.ToString() ?? "" : "";
    }

    private static string[] Split(string? value)
        => value.IsNullOrEmpty()
            ? []
            : value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
}
