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
        var strLink = string.Empty;

        switch (userActivity.ActivityType)
        {
            case "NSUserActivityTypeBrowsingWeb":
                strLink = userActivity.WebPageUrl.AbsoluteString;
                break;
            case "com.apple.corespotlightitem":
                if (userActivity.UserInfo?.ContainsKey(CSSearchableItem.ActivityIdentifier) == true)
                    strLink = userActivity.UserInfo.ObjectForKey(CSSearchableItem.ActivityIdentifier).ToString();
                break;
            default:
                if (userActivity.UserInfo?.ContainsKey(new NSString("link")) == true)
                    strLink = userActivity.UserInfo[new NSString("link")].ToString();
                break;
        }

        if (!string.IsNullOrEmpty(strLink))
            App.Current.SendOnAppLinkRequestReceived(new Uri(strLink));
    }
}
