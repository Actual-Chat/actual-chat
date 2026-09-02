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

    protected override MauiApp CreateMauiApp()
        => MauiProgram.CreateMauiApp();

    public override void DidFinishLaunching(NSNotification notification)
    {
        base.DidFinishLaunching(notification);
        UNUserNotificationCenter.Current.Delegate = MacOSNotificationDelegate.Instance;
    }

    public override void OpenUrls(NSApplication application, NSUrl[] urls)
    {
        foreach (var url in urls)
            if (url.AbsoluteString is { } urlString)
                UrlOpened?.Invoke(urlString);
    }
}
