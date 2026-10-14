using Foundation;

namespace ActualChat.App.Maui;

[Register("SceneDelegate")]
public sealed class SceneDelegate : MauiUISceneDelegate
{
    public override void WillConnect(UIKit.UIScene scene, UIKit.UISceneSession session,
        UIKit.UISceneConnectionOptions connectionOptions)
    {
        base.WillConnect(scene, session, connectionOptions);
        if (connectionOptions.UrlContexts.Count > 0)
            OpenUrl(scene, connectionOptions.UrlContexts);
        foreach (var userActivity in connectionOptions.UserActivities)
            ContinueUserActivity(scene, userActivity);
    }
}
