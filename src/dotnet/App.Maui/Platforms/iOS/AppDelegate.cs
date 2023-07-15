using CoreSpotlight;
using Foundation;
using UIKit;

namespace ActualChat.App.Maui;

[Register("AppDelegate")]
public class AppDelegate : MauiUIApplicationDelegate
{
    protected override MauiApp CreateMauiApp()
    {
        NSHttpCookieStorage.SharedStorage.AcceptPolicy = NSHttpCookieAcceptPolicy.Always;
        return MauiProgram.CreateMauiApp();
    }

    public override bool ContinueUserActivity(
        UIApplication application,
        NSUserActivity userActivity,
        UIApplicationRestorationHandler completionHandler)
    {
        CheckForAppLink(userActivity);
        return base.ContinueUserActivity(application, userActivity, completionHandler);
    }

    private void CheckForAppLink(NSUserActivity userActivity)
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
            App.Current!.SendOnAppLinkRequestReceived(url.ToUri());
    }
}
