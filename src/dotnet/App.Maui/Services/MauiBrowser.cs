#if IOS
using Foundation;
using UIKit;
#endif

namespace ActualChat.App.Maui.Services;

/// <summary>
/// Platform-specific browser URL opening with iOS workarounds.
/// </summary>
public static class MauiBrowser
{
    public static Task<bool> Open(string url)
    {
#if IOS
        return DispatchToMainThread(() => UIApplication.SharedApplication.OpenUrlAsync(new NSUrl(url), new UIApplicationOpenUrlOptions()));
#else
        return Browser.Default.OpenAsync(url, BrowserLaunchMode.External);
#endif
    }
}
