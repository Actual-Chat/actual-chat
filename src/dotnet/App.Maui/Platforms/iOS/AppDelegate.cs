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

        // The window is the bottom-most layer and white by default, so it shows through until WebKit paints.
        Log.LogWarning("DIAGNOSTIC: Window={Window}, RootVC={RootVC}", Window?.ToString() ?? "<null>", Window?.RootViewController?.ToString() ?? "<null>");
        if (Window is { } window) {
            window.BackgroundColor = UIColor.Red; // DIAGNOSTIC
            if (window.RootViewController?.View is { } rootView)
                rootView.BackgroundColor = UIColor.Cyan; // DIAGNOSTIC
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
    // iOS applies aps.badge from the payload automatically; we only drop the dismissed banners.
    [Export("application:didReceiveRemoteNotification:fetchCompletionHandler:")]
    public void DidReceiveRemoteNotification(
        UIApplication application, NSDictionary userInfo, Action<UIBackgroundFetchResult> completionHandler)
    {
        try {
            var dismissedTags = (userInfo[new NSString(Constants.Notification.MessageDataKeys.DismissedTags)] as NSString)?.ToString();
            if (!dismissedTags.IsNullOrEmpty()) {
                var tags = dismissedTags.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                RemoveDeliveredNotifications(tags);
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

    // Removes delivered notifications whose thread (= chat tag) is being dismissed.
    private static void RemoveDeliveredNotifications(IReadOnlyCollection<string> dismissedTags)
        => UNUserNotificationCenter.Current.GetDeliveredNotifications(delivered => {
            var toRemove = delivered
                .Where(n => dismissedTags.Contains(n.Request.Content.ThreadIdentifier))
                .Select(n => n.Request.Identifier)
                .ToArray();
            if (toRemove.Length > 0)
                UNUserNotificationCenter.Current.RemoveDeliveredNotifications(toRemove);
        });

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
                url = userActivity.UserInfo[new NSString("link")].ToString();
            break;
        }

        if (!url.IsNullOrEmpty())
            App.Current.SendOnAppLinkRequestReceived(url.ToUri());
    }

    private static void SetBackgroundState(bool isBackground)
        => MauiBackgroundState.Set(isBackground);
}
