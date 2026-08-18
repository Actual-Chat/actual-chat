using ActualChat.UI.Blazor.Services;
using ActualLab.Diagnostics;
using Android.Content;

namespace ActualChat.App.Maui;

public static class NotificationHandler
{
    private static readonly ILogger Log = StaticLog.For(typeof(NotificationHandler));
    private static ILogger? DebugLog => Log.IfEnabled(LogLevel.Information, Constants.DebugMode.AndroidIncomingCalls);

    public static void HandleIntent(Intent intent, bool isColdStart = false)
    {
        if (NotificationHelper.NotificationViewAction != intent.Action)
            return;

        if (intent.GetBooleanExtra(MainActivity.RequestMicrophonePermissionExtraKey, false)) {
            // Only an activity can show the permission dialog, which is the whole reason the
            // microphone-blocked notification exists - the reply that hit this had no way there.
            MicrophoneBlockedNotification.Dismiss();
            MainActivity.Current.RequestMicrophonePermission();
        }

        if (intent.GetBooleanExtra(IncomingCallNotifications.FullScreenExtraKey, false)) {
            DebugLog?.LogInformation("CALL_TRACE: HandleIntent full-screen intent, isColdStart={IsColdStart}", isColdStart);
            // Over the lock screen we don't navigate to the chat (it opens on go-to-chat). On a cold
            // start the WebView boots to a restored route that would flash, so bring the app over the
            // keyguard eagerly behind a splash-colored cover. When the app is already running, don't
            // cover or show-over-lock yet — the call screen renders first (behind the keyguard, the
            // live WebView draws it) and RevealCallScreen brings it over the lock once it's drawn.
            if (isColdStart) {
                var activity = MainActivity.Current;
                activity.ShowCallCover();
                activity.EnableShowWhenLocked();
            }
            else
                DebugLog?.LogInformation("CALL_TRACE: warm start — no cover, render-first-then-reveal");
            IncomingCallNotifications.HandleViewIntent(intent);
            return;
        }

        AppNavigationQueue.EnqueueOrNavigateToUrl(intent.Data?.ToString(), AutoNavigationReason.Notification);
        IncomingCallNotifications.HandleViewIntent(intent);
    }
}
