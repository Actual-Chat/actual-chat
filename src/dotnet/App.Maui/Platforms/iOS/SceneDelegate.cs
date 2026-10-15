using ActualChat.App.Maui.Services;
using ActualChat.Maui.Services;
using Foundation;
using Intents;
using UIKit;

namespace ActualChat.App.Maui;

[Register("SceneDelegate")]
public sealed class SceneDelegate : MauiUISceneDelegate
{
    public override void WillConnect(UIScene scene, UISceneSession session, UISceneConnectionOptions connectionOptions)
    {
        base.WillConnect(scene, session, connectionOptions);

        // The scene owns the window now; FinishedLaunching runs before it exists.
        if (Window is { } window) {
            var backgroundColor = UIColor.FromRGB(
                MauiSettings.SplashBackgroundColor.Red,
                MauiSettings.SplashBackgroundColor.Green,
                MauiSettings.SplashBackgroundColor.Blue);
            window.BackgroundColor = backgroundColor;
            if (window.RootViewController?.View is { } rootView)
                rootView.BackgroundColor = backgroundColor;
        }

        // The Xcode 27 bindings hand out null here on a plain launch, not an empty set.
        if (connectionOptions.UserActivities is { } userActivities)
            foreach (var userActivity in userActivities)
                ContinueUserActivity(scene, userActivity);
        if (connectionOptions.UrlContexts is { Count: > 0 } urlContexts)
            OpenUrl(scene, urlContexts);
    }

    public override bool ContinueUserActivity(UIScene scene, NSUserActivity userActivity)
    {
        var intent = userActivity.GetInteraction()?.Intent;
        if (intent is INStartCallIntent or INStartVideoCallIntent)
            IosCalls.Instance.ContinueCall(intent);
        else {
            var url = userActivity.GetAppLinkUrl();
            if (!url.IsNullOrEmpty())
                App.Current.SendOnAppLinkRequestReceived(url.ToUri());
        }

        return base.ContinueUserActivity(scene, userActivity);
    }

    public override void OnActivated(UIScene scene)
    {
        MauiBackgroundState.SetIsBackground(false);
        base.OnActivated(scene);
    }

    public override void DidEnterBackground(UIScene scene)
    {
        // Keep the assertion until suspend handlers release their file locks.
        using (UIApplication.SharedApplication.BeginBackgroundTaskScope("ActualChat.Suspend"))
            MauiBackgroundState.SetIsBackground(true);
        base.DidEnterBackground(scene);
    }
}
