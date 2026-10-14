using AppKit;
using Foundation;
using Microsoft.Maui.Platforms.MacOS.Platform;
using UserNotifications;

namespace ActualChat.App.Maui;

[Register("AppDelegate")]
public class AppDelegate : MacOSMauiApplication
{
    // Raised for voxt-dev:// activations (see CFBundleURLTypes in Info.plist);
    // MauiWebAuthenticator completes its sign-in flow on the auth-complete callback.
    public static event Action<string>? UrlOpened;
    private static readonly ILogger Log = StaticLog.For<AppDelegate>();

    private NSObject[] _powerObservers = [];

    protected override MauiApp CreateMauiApp()
        => MauiProgram.CreateMauiApp();

    public override void DidFinishLaunching(NSNotification notification)
    {
        base.DidFinishLaunching(notification);
        UNUserNotificationCenter.Current.Delegate = MacOSNotificationDelegate.Instance;
        LogPowerEvents();
    }

    public override bool ApplicationShouldHandleReopen(NSApplication sender, bool hasVisibleWindows)
        => hasVisibleWindows || !WindowConfigurator.TryShowWindow();

    // Universal links (applinks: in the entitlements) arrive here, not in OpenUrls
    public override bool ContinueUserActivity(
        NSApplication application,
        NSUserActivity userActivity,
        ContinueUserActivityRestorationHandler restorationHandler)
    {
        var url = userActivity.GetAppLinkUrl();
        if (url.IsNullOrEmpty())
            return false;

        App.Current.SendOnAppLinkRequestReceived(url.ToUri());
        return true;
    }

    public override void OpenUrls(NSApplication application, NSUrl[] urls)
    {
        foreach (var url in urls)
            if (url.AbsoluteString is { } urlString)
                UrlOpened?.Invoke(urlString);
    }

    // Private methods

    private void LogPowerEvents()
        // Banners are posted over the app's own RPC, so a gap in them has to be told apart
        // from the Mac having been asleep
        => _powerObservers = [
            NSWorkspace.Notifications.ObserveWillSleep((_, _) => Log.LogInformation("System will sleep")),
            NSWorkspace.Notifications.ObserveDidWake((_, _) => Log.LogInformation("System did wake")),
            NSWorkspace.Notifications.ObserveScreensDidSleep((_, _) => Log.LogInformation("Screens did sleep")),
            NSWorkspace.Notifications.ObserveScreensDidWake((_, _) => Log.LogInformation("Screens did wake")),
        ];
}
