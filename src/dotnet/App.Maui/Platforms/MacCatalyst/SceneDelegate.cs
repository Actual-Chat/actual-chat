using Foundation;

namespace ActualChat.App.Maui;

[Register("SceneDelegate")]
public sealed class SceneDelegate : MauiUISceneDelegate
{
    public override void WillConnect(UIKit.UIScene scene, UIKit.UISceneSession session,
        UIKit.UISceneConnectionOptions connectionOptions)
    {
        base.WillConnect(scene, session, connectionOptions);
        // The Xcode 27 bindings hand out null here on a plain launch, not an empty set.
        if (connectionOptions.UrlContexts is { Count: > 0 } urlContexts)
            OpenUrl(scene, urlContexts);
        if (connectionOptions.UserActivities is { } userActivities)
            foreach (var userActivity in userActivities)
                ContinueUserActivity(scene, userActivity);
    }
}
