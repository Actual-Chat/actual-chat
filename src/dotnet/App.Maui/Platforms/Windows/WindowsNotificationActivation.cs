using ActualChat.Notifications;
using ActualChat.UI.Blazor.App.Services;
using ActualChat.UI.Blazor.Services;
using Microsoft.Windows.AppNotifications;

namespace ActualChat.App.Maui;

/// <summary>
/// The app's registration with the Windows notification platform: routes toast clicks and
/// incoming-call buttons into the app and reports whether toasts are enabled for it.
/// </summary>
public static class WindowsNotificationActivation
{
    public const string ActionKey = "action";
    public const string AcceptCallAction = "accept-call";
    public const string DeclineCallAction = "decline-call";

    private static int _isRegistered;
    // Register runs before MauiDiagnostics sets up logging, so its failure waits here for the first reader
    private static Exception? _registrationError;

    public static bool IsEnabled
        => IsRegistered && AppNotificationManager.Default.Setting == AppNotificationSetting.Enabled;

    public static bool IsRegistered {
        get {
            if (Interlocked.Exchange(ref _registrationError, null) is { } error)
                StaticLog.For(typeof(WindowsNotificationActivation))
                    .LogWarning(error, "App notifications are unavailable - no toasts for this session");
            return Volatile.Read(ref _isRegistered) != 0;
        }
    }

    public static void Register()
    {
        // Must run before AppInstance.GetActivatedEventArgs, or a launch from a toast click
        // arrives as a plain launch.
        try {
            if (!AppNotificationManager.IsSupported())
                return;

            var manager = AppNotificationManager.Default;
            manager.NotificationInvoked += (_, e) => Handle(e);
            manager.Register();
            Volatile.Write(ref _isRegistered, 1);
        }
        catch (Exception e) {
            Volatile.Write(ref _registrationError, e);
        }
    }

    public static void Handle(AppNotificationActivatedEventArgs e)
    {
        var arguments = e.Arguments;
        arguments.TryGetValue(Constants.Notification.MessageDataKeys.Tag, out var tag);
        arguments.TryGetValue(ActionKey, out var action);
        var ringCallId = NotificationExt.TryParseCallTag(tag);
        switch (action) {
        case AcceptCallAction when ringCallId is { } callId:
            WindowConfigurator.ShowWindow();
            DispatchToCallScreens(x => x.Accept(callId.ChatId, callId), "CallScreensUI.Accept");
            break;
        case DeclineCallAction when ringCallId is { } callId:
            DispatchToCallScreens(x => x.Decline(callId.ChatId, callId), "CallScreensUI.Decline");
            break;
        default:
            if (arguments.TryGetValue(Constants.Notification.MessageDataKeys.Link, out var url)
                && !url.IsNullOrEmpty()) {
                AppNavigationQueue.EnqueueOrNavigateToUrl(url, AutoNavigationReason.Notification);
                WindowConfigurator.ShowWindow();
            }
            break;
        }
    }

    // Private methods

    private static void DispatchToCallScreens(Func<CallScreensUI, Task> action, string name)
        => _ = DispatchToBlazor(c => action.Invoke(c.GetRequiredService<CallScreensUI>()), name);
}
