using ActualChat.Maui.Services;
using ActualChat.Notifications;
using ActualChat.UI.Blazor.App.Services;
using ActualChat.UI.Blazor.Services;
using AppKit;
using UserNotifications;

namespace ActualChat.App.Maui;

/// <summary>
/// Routes macOS notification taps and incoming-call actions into the app, and suppresses banners
/// while the window is on screen and active - the in-app UI covers that case.
/// </summary>
public sealed class MacOSNotificationDelegate : UNUserNotificationCenterDelegate
{
    public const string CallCategoryId = "incoming-call";
    public const string AcceptCallActionId = "accept-call";
    public const string DeclineCallActionId = "decline-call";

    public static readonly MacOSNotificationDelegate Instance = new();

    public override void WillPresentNotification(
        UNUserNotificationCenter center,
        UNNotification notification,
        Action<UNNotificationPresentationOptions> completionHandler)
        // Called only while the app is active, which it stays with its window hidden or minimized
        => completionHandler.Invoke(MauiBackgroundState.IsBackground.Value
            ? UNNotificationPresentationOptions.Banner | UNNotificationPresentationOptions.List
                | UNNotificationPresentationOptions.Sound
            : UNNotificationPresentationOptions.None);

    public override void DidReceiveNotificationResponse(
        UNUserNotificationCenter center,
        UNNotificationResponse response,
        Action completionHandler)
    {
        var content = response.Notification.Request.Content;
        var callChatId = NotificationExt.TryParseCallTag(content.ThreadIdentifier);
        switch (response.ActionIdentifier) {
        case AcceptCallActionId when callChatId is { } chatId:
            ShowApp();
            DispatchToCallScreens(x => x.Accept(chatId), "CallScreensUI.Accept");
            break;
        case DeclineCallActionId when callChatId is { } chatId:
            DispatchToCallScreens(x => x.Decline(chatId), "CallScreensUI.Decline");
            break;
        default:
            var link = content.UserInfo[Constants.Notification.MessageDataKeys.Link];
            var url = (link as Foundation.NSString)?.ToString();
            if (!url.IsNullOrEmpty()) {
                AppNavigationQueue.EnqueueOrNavigateToUrl(url, AutoNavigationReason.Notification);
                ShowApp();
            }
            break;
        }
        completionHandler.Invoke();
    }

    // Private methods

    private static void DispatchToCallScreens(Func<CallScreensUI, Task> action, string name)
        => _ = DispatchToBlazor(c => action.Invoke(c.GetRequiredService<CallScreensUI>()), name);

    private static void ShowApp()
        // Activation alone leaves a window hidden by the red button off screen
        => BeginDispatchToMainThread(() => {
            WindowConfigurator.TryShowWindow();
            NSApplication.SharedApplication.ActivateIgnoringOtherApps(true);
        });
}
