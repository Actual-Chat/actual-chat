using ActualChat.App.Maui.Services;
using ActualChat.Maui.Services;
using CoreSpotlight;
using Firebase.CloudMessaging;
using Foundation;
using UIKit;
using UserNotifications;
using DeviceType = ActualChat.Notifications.DeviceType;
using ILogger = Microsoft.Extensions.Logging.ILogger;

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

    public override bool FinishedLaunching(UIApplication application, NSDictionary? launchOptions)
    {
        RegisterBadgeNotifications();
        var result = base.FinishedLaunching(application, launchOptions);

        // The root view controller's view is white by default and shows for a frame between the
        // launch screen and WebKit's first paint - that's the white blink. The window is below it.
        if (Window is { } window) {
            var backgroundColor = UIColor.FromRGB(
                MauiSettings.SplashBackgroundColor.Red,
                MauiSettings.SplashBackgroundColor.Green,
                MauiSettings.SplashBackgroundColor.Blue);
            window.BackgroundColor = backgroundColor;
            if (window.RootViewController?.View is { } rootView)
                rootView.BackgroundColor = backgroundColor;
        }

        return result;
    }

    public override bool ContinueUserActivity(
        UIApplication application,
        NSUserActivity userActivity,
        UIApplicationRestorationHandler completionHandler)
    {
        CheckForAppLink(userActivity);
        return base.ContinueUserActivity(application, userActivity, completionHandler);
    }

    public override void OnActivated(UIApplication application)
    {
        SetBackgroundState(false);
        base.OnActivated(application);
    }

    public override void DidEnterBackground(UIApplication application)
    {
        // Hold the assertion until suspend handlers (incl. closing the Kvasar stores) finish;
        // otherwise iOS suspends us with file locks held -> 0xdead10cc.
        using (application.BeginBackgroundTaskScope("ActualChat.Suspend"))
            SetBackgroundState(true);
        base.DidEnterBackground(application);
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
                RemoveDeliveredNotifications(Split(dismissedIds), Split(dismissedTags));
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

    private static void RegisterBadgeNotifications()
        => UNUserNotificationCenter.Current.RequestAuthorization(
            UNAuthorizationOptions.Badge | UNAuthorizationOptions.Alert | UNAuthorizationOptions.Sound,
            (approved, error) => {
                if (approved)
                    return;

                // Handle the case where the user did not grant permission
                if (error != null!)
                    Log.LogError("Error requesting notification authorization: {Error}", error);
                Log.LogWarning("Badge notification authorization denied");

            });

    private static void CheckForAppLink(NSUserActivity userActivity)
    {
        var url = "";
        switch (userActivity.ActivityType) {
        case "NSUserActivityTypeBrowsingWeb":
            url = userActivity.WebPageUrl!.AbsoluteString;
            break;
        case "com.apple.corespotlightitem":
            if (userActivity.UserInfo?.ContainsKey(CSSearchableItem.ActivityIdentifier) == true)
                url = userActivity.UserInfo.ObjectForKey(CSSearchableItem.ActivityIdentifier)!.ToString();
            break;
        default:
            if (userActivity.UserInfo?.ContainsKey(new NSString("link")) == true)
                url = userActivity.UserInfo[new NSString("link")]!.ToString();
            break;
        }

        if (!url.IsNullOrEmpty())
            App.Current.SendOnAppLinkRequestReceived(url.ToUri());
    }

    private static void SetBackgroundState(bool isBackground)
        => MauiBackgroundState.Set(isBackground);
}
