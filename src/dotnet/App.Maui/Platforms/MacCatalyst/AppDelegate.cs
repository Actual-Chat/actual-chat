using Firebase.CloudMessaging;
using Foundation;
using UIKit;
using DeviceType = ActualChat.Notifications.DeviceType;
using ILogger = Microsoft.Extensions.Logging.ILogger;

namespace ActualChat.App.Maui;

[Register("AppDelegate")]
public class AppDelegate : MauiUIApplicationDelegate, IMessagingDelegate
{
    private static ILogger? _log;
    private static ILogger Log => _log ??= StaticLog.Factory.CreateLogger<AppDelegate>();
    protected override MauiApp CreateMauiApp()
        => MauiProgram.CreateMauiApp();

    public override bool FinishedLaunching(UIApplication application, NSDictionary? launchOptions)
    {
        // MacCatalyst has no Plugin.Firebase binding, so wire FCM up via the AdamE native API.
        Firebase.Core.App.Configure();
        Firebase.CloudMessaging.Messaging.SharedInstance.AutoInitEnabled = true;
        Firebase.CloudMessaging.Messaging.SharedInstance.Delegate = this;
        application.RegisterForRemoteNotifications();
        return base.FinishedLaunching(application, launchOptions);
    }

    [Export("application:didRegisterForRemoteNotificationsWithDeviceToken:")]
    public void RegisteredForRemoteNotifications(UIApplication application, NSData deviceToken)
        => Firebase.CloudMessaging.Messaging.SharedInstance.ApnsToken = deviceToken;

    [Export("application:didFailToRegisterForRemoteNotificationsWithError:")]
    public void FailedToRegisterForRemoteNotifications(UIApplication application, NSError error)
        => Log.LogWarning("Failed to register for remote notifications: {Error}", error);

    [Export("application:didReceiveRemoteNotification:fetchCompletionHandler:")]
    public void DidReceiveRemoteNotification(
        UIApplication application, NSDictionary userInfo, Action<UIBackgroundFetchResult> completionHandler)
        => completionHandler(AppleNotifications.HandleRemoteNotification(userInfo, Log));

    [Export("messaging:didReceiveRegistrationToken:")]
    public void DidReceiveRegistrationToken(Firebase.CloudMessaging.Messaging messaging, string fcmToken)
        => AppleNotifications.ForwardRegistrationToken(fcmToken, DeviceType.MacOSApp, Log);
}
