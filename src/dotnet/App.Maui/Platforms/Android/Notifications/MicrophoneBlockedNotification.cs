using ActualChat.Localization;
using Android.App;
using Android.Content;
using AndroidX.Core.App;
using Microsoft.Extensions.Localization;
using Application = Android.App.Application;
using ILogger = Microsoft.Extensions.Logging.ILogger;

namespace ActualChat.App.Maui;

/// <summary>
/// Tells the user a push-to-talk reply couldn't open the microphone, from the lock screen if
/// that's where they are. A notification can't grant a runtime permission - only a foregrounded
/// activity can - so this exists to get them there.
/// </summary>
public static class MicrophoneBlockedNotification
{
    public const string ChannelId = "microphone_blocked";
    private const string Tag = "microphone-blocked";
    private static ILogger? _log;

    private static Context Context => Application.Context;
    private static IStringLocalizer L => AppStrings.L;
    private static ILogger Log => _log ??= StaticLog.Factory.CreateLogger(typeof(MicrophoneBlockedNotification));

    public static void ShowPermissionDenied()
        => Show(L.MicBlocked_Title_Format(CoreConstants.AppName), L.MicBlocked_PermissionDenied);

    public static void ShowUnavailable()
        // Opening the app really is the fix: only an activity can re-earn the mic grant.
        => Show(L.MicBlocked_Title_Format(CoreConstants.AppName),
            L.MicBlocked_Unavailable_Format(CoreConstants.AppName));

    private static void Show(string title, string text)
    {
        try {
            EnsureChannelExists();
            var intent = NotificationHelper.CreateViewIntent(Context, Links.Chats)!;
            intent.PutExtra(MainActivity.RequestMicrophonePermissionExtraKey, true);
            var pendingIntent = PendingIntent.GetActivity(Context,
                NotificationHelper.RequestCodeProvider.IncrementAndGet(),
                intent, PendingIntentFlags.OneShot | PendingIntentFlags.Immutable);

            var builder = new NotificationCompat.Builder(Context, ChannelId)
                // ReSharper disable once AccessToStaticMemberViaDerivedType
                .SetSmallIcon(Microsoft.Maui.Resource.Drawable.notification_app_icon)!
                .SetContentTitle(title)!
                .SetContentText(text)!
                .SetCategory(Android.App.Notification.CategoryError)!
                .SetPriority((int)NotificationPriority.High)!
                // Public, or the one place this matters - the lock screen - shows an empty shell.
                .SetVisibility(NotificationCompat.VisibilityPublic)!
                .SetAutoCancel(true)!
                .SetContentIntent(pendingIntent)!;
            if (CanUseFullScreenIntent())
                _ = builder.SetFullScreenIntent(pendingIntent, true);

            NotificationManagerCompat.From(Context)!.Notify(Tag, 0, builder.Build());
        }
        catch (Exception e) {
            // Posting needs POST_NOTIFICATIONS on API 33+; a denied permission must not take the
            // reply path down with it.
            Log.LogWarning(e, "Couldn't post the microphone-blocked notification");
        }
    }

    public static void Dismiss()
    {
        try {
            NotificationManagerCompat.From(Context)!.Cancel(Tag, 0);
        }
        catch (Exception e) {
            Log.LogWarning(e, "Couldn't cancel the microphone-blocked notification");
        }
    }

    // Private methods

    private static bool CanUseFullScreenIntent()
    {
        // Android 14+ auto-grants USE_FULL_SCREEN_INTENT only to calling and alarm apps, and the
        // user can revoke it. Without it a full-screen intent is silently ignored, so the
        // high-importance heads-up above is the whole notification rather than a consolation.
        if (!OperatingSystem.IsAndroidVersionAtLeast(34))
            return true;

        var manager = (NotificationManager)Context.GetSystemService(Context.NotificationService)!;
        var canUse = manager.CanUseFullScreenIntent();
        if (!canUse)
            Log.LogInformation("Full-screen intent isn't permitted; falling back to a heads-up notification");

        return canUse;
    }

    private static void EnsureChannelExists()
    {
        var notificationManager = (NotificationManager)Context.GetSystemService(Context.NotificationService)!;
        var channel = new NotificationChannel(
            ChannelId, L.NotificationChannel_MicrophoneProblems, NotificationImportance.High) {
            LockscreenVisibility = NotificationVisibility.Public,
        };
        notificationManager.CreateNotificationChannel(channel);
    }
}
