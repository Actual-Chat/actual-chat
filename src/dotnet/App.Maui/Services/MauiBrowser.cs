#if IOS || MACCATALYST
using Foundation;
using UIKit;
#endif

namespace ActualChat.App.Maui.Services;

public static class MauiBrowser
{
    public static Task<bool> Open(string url)
    {
#if IOS || MACCATALYST
        return UIApplication.SharedApplication.OpenUrlAsync(new NSUrl(url), new UIApplicationOpenUrlOptions());
#else
        return Browser.Default.OpenAsync(url, BrowserLaunchMode.External);
#endif
    }
}
