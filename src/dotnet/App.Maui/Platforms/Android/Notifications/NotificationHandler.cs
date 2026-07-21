using ActualChat.UI.Blazor.Services;
using Android.Content;

namespace ActualChat.App.Maui;

public static class NotificationHandler
{
    public static void HandleIntent(Intent intent)
    {
        if (NotificationHelper.NotificationViewAction != intent.Action)
            return;

        if (intent.GetBooleanExtra(IncomingCallNotifications.FullScreenExtraKey, false))
            MainActivity.Current.EnableShowWhenLocked();

        AppNavigationQueue.EnqueueOrNavigateToUrl(intent.Data?.ToString(), AutoNavigationReason.Notification);
        IncomingCallNotifications.HandleViewIntent(intent);
    }
}
