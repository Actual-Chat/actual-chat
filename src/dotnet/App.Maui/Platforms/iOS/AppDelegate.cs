using ActualChat.UI.Blazor.Services;
using CoreSpotlight;
using Foundation;
using UIKit;

namespace ActualChat.App.Maui;

[Register("AppDelegate")]
public class AppDelegate : UIApplicationDelegate
{
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
        _ = DispatchToBlazor(
            c => c.GetRequiredService<IBackgroundStateHandler>().SetIsBackground(false),
            "IBackgroundStateHandler.SetBackgroundState(false)");
        base.OnActivated(application);
    }

    public override void DidEnterBackground(UIApplication application)
    {
        _ = DispatchToBlazor(
            c => c.GetRequiredService<IBackgroundStateHandler>().SetIsBackground(true),
            "IBackgroundStateHandler.SetBackgroundState(true)");
        base.DidEnterBackground(application);
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
}
