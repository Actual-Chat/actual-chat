using CoreSpotlight;
using Foundation;

namespace ActualChat.App.Maui;

public static class NSUserActivityExt
{
    public static string GetAppLinkUrl(this NSUserActivity userActivity)
    {
        switch (userActivity.ActivityType) {
        case "NSUserActivityTypeBrowsingWeb":
            return userActivity.WebPageUrl?.AbsoluteString ?? "";
        case "com.apple.corespotlightitem":
            return userActivity.UserInfo?.ContainsKey(CSSearchableItem.ActivityIdentifier) == true
                ? userActivity.UserInfo.ObjectForKey(CSSearchableItem.ActivityIdentifier)!.ToString()
                : "";
        default:
            return userActivity.UserInfo?.ContainsKey(new NSString("link")) == true
                ? userActivity.UserInfo[new NSString("link")]!.ToString()
                : "";
        }
    }
}
