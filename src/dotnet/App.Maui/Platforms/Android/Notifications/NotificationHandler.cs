using ActualChat.UI.Blazor.Services;
using Android.Content;

namespace ActualChat.App.Maui;

public static class NotificationHandler
{
    public static void HandleIntent(Intent intent)
    {
        if (!OrdinalEquals(NotificationHelper.NotificationViewAction, intent.Action))
            return;

        AppNavigationQueue.EnqueueOrNavigateToNotificationUrl(intent.Data?.ToString());
    }
}
