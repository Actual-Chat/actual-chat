using ActualChat.UI.Blazor.Services;
using AppKit;
using UserNotifications;

namespace ActualChat.App.Maui;

/// <summary>
/// Routes macOS notification taps into the app (activate + navigate to the chat link) and
/// suppresses banners while the app is frontmost - the in-app UI covers that case.
/// </summary>
public sealed class MacOSNotificationDelegate : UNUserNotificationCenterDelegate
{
    public static readonly MacOSNotificationDelegate Instance = new();

    public override void WillPresentNotification(
        UNUserNotificationCenter center,
        UNNotification notification,
        Action<UNNotificationPresentationOptions> completionHandler)
        // Called only while the app is frontmost, where the open chat UI already shows the update
        => completionHandler.Invoke(UNNotificationPresentationOptions.None);

    public override void DidReceiveNotificationResponse(
        UNUserNotificationCenter center,
        UNNotificationResponse response,
        Action completionHandler)
    {
        var userInfo = response.Notification.Request.Content.UserInfo;
        var url = (userInfo[Constants.Notification.MessageDataKeys.Link] as Foundation.NSString)?.ToString();
        if (!url.IsNullOrEmpty()) {
            AppNavigationQueue.EnqueueOrNavigateToUrl(url, AutoNavigationReason.Notification);
            BeginDispatchToMainThread(() => NSApplication.SharedApplication.ActivateIgnoringOtherApps(true));
        }
        completionHandler.Invoke();
    }
}
