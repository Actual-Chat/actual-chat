using Android.App;
using AndroidX.Core.App;
using ActualChat.UI.Blazor.App.Services;

namespace ActualChat.App.Maui;

// Android IDeviceNotifications:
// - prune: closes shown notifications whose tag is no longer active;
// - create: re-shows a newly-active notification that isn't currently shown (heals a dropped push).
// Only newly-added tags are passed as create candidates, so a user-swiped banner (active set
// unchanged) is never resurrected — no dismissal tracking needed.
public class AndroidDeviceNotifications : IDeviceNotifications
{
    public Task Reconcile(
        IReadOnlyList<ActiveNotificationInfo> active,
        IReadOnlyCollection<string> createTags,
        CancellationToken cancellationToken)
    {
        var activeTags = active.Select(x => x.Tag).ToHashSet(StringComparer.Ordinal);
        var notificationManager = NotificationManagerCompat.From(Application.Context);

        var shownTags = new HashSet<string>(StringComparer.Ordinal);
        var shown = notificationManager.ActiveNotifications;
        if (shown != null)
            foreach (var statusBarNotification in shown) {
                var tag = statusBarNotification.Tag;
                if (tag.IsNullOrEmpty())
                    continue;
                if (!activeTags.Contains(tag))
                    notificationManager.Cancel(tag, statusBarNotification.Id);
                else
                    shownTags.Add(tag);
            }

        foreach (var tag in createTags) {
            if (shownTags.Contains(tag))
                continue;
            var info = active.FirstOrDefault(x => x.Tag == tag);
            if (info != null)
                Show(notificationManager, info);
        }
        return Task.CompletedTask;
    }

    // Mirrors FirebaseMessagingService.ShowChatMessageNotification (kept separate so the proven
    // push path is untouched).
    private static void Show(NotificationManagerCompat notificationManager, ActiveNotificationInfo info)
    {
        var context = Application.Context;
        var contentIntent = NotificationHelper.CreateViewIntent(context, info.Url);
        var contentPendingIntent = PendingIntent.GetActivity(context, 0,
            contentIntent, PendingIntentFlags.OneShot | PendingIntentFlags.Immutable);

        var notificationBuilder = new NotificationCompat.Builder(context, NotificationHelper.Constants.DefaultChannelId)
            .SetContentTitle(info.Title)!
            .SetSmallIcon(Resource.Drawable.notification_app_icon)!
            .SetColor(0x0036A3)!
            .SetContentText(info.Text)!
            .SetContentIntent(contentPendingIntent)!
            .SetAutoCancel(true)!
            .SetPriority((int)NotificationPriority.High)!;
        if (!info.IconUrl.IsNullOrEmpty()) {
            var largeImage = NotificationHelper.GetImage(info.IconUrl);
            if (largeImage != null)
                notificationBuilder.SetLargeIcon(largeImage);
        }
        notificationManager.Notify(info.Tag, 0, notificationBuilder.Build());
    }
}
