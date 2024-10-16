using ActualChat.App.Maui.Services;
using ActualChat.Notification;
using ActualChat.Security;
using ActualChat.UI.Blazor.Services;
using ActualLab.Rpc;
using CoreSpotlight;
using Firebase.CloudMessaging;
using Foundation;
using UIKit;
using DeviceType = ActualChat.Notification.DeviceType;
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

    public override bool FinishedLaunching(UIApplication application, NSDictionary launchOptions)
    {
        var settings = UIUserNotificationSettings.GetSettingsForTypes(UIUserNotificationType.Badge, null);
        UIApplication.SharedApplication.RegisterUserNotificationSettings(settings);
        return base.FinishedLaunching(application, launchOptions);
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
        SetBackgroundState(true);
        base.DidEnterBackground(application);
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

    private static void CheckForAppLink(NSUserActivity userActivity)
    {
        var url = "";
        switch (userActivity.ActivityType) {
        case "NSUserActivityTypeBrowsingWeb":
            url = userActivity.WebPageUrl!.AbsoluteString;
            break;
        case "com.apple.corespotlightitem":
            if (userActivity.UserInfo?.ContainsKey(CSSearchableItem.ActivityIdentifier) == true)
                url = userActivity.UserInfo.ObjectForKey(CSSearchableItem.ActivityIdentifier).ToString();
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
        => MauiBackgroundStateTracker.SetBackgroundState(isBackground);
}
