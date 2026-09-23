using AndroidX.Core.App;
using ActualChat.Notifications;
using ActualChat.UI.Blazor.App.Services;

namespace ActualChat.App.Maui;

// Android IDeviceNotifications:
// - prune: closes shown notifications whose tag is no longer active, rings excepted;
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
        var activeTags = active.Select(x => x.Tag).ToHashSet();
        var notificationManager = NotificationManagerCompat.From(Android.App.Application.Context);

        var shownTags = new HashSet<string>();
        var shown = notificationManager?.ActiveNotifications;
        if (shown != null)
            foreach (var statusBarNotification in shown) {
                // Only banners this app posted for a push tag are the active set's to prune: the
                // tray also holds the foreground-service, upload, attention and microphone
                // notifications, none of which is ever an active tag.
                var tag = NotificationHelper.GetPushBannerTag(statusBarNotification.Notification!);
                if (tag.IsNullOrEmpty())
                    continue;

                // A ring is never the active set's to close, even when the set doesn't list it:
                // the set read here can be older than the push that posted the banner, and the
                // full-screen intent brings the app forward - which is what triggers this prune.
                // Closing it costs the only way to answer wherever that intent is gated off.
                // The ring ends on its own SetTimeoutAfter(RingTimeout), on the dismissal push,
                // or when the call screen takes over.
                if (activeTags.Contains(tag) || NotificationExt.TryParseCallTag(tag) is not null)
                    shownTags.Add(tag);
                else
                    notificationManager?.Cancel(tag, statusBarNotification.Id);
            }

        foreach (var tag in createTags) {
            if (shownTags.Contains(tag))
                continue;
            var info = active.FirstOrDefault(x => x.Tag == tag);
            if (info == null)
                continue;

            if (NotificationExt.TryParseCallTag(tag) is { } callChatId)
                // A ring must come back as a ring — CallStyle, action buttons, full-screen intent —
                // and it must alert: unlike a message banner, a silent call is useless.
                IncomingCallNotifications.Show(callChatId, tag, info.Url, info.Title, info.IconUrl);
            else
                // Healing a dropped banner must not alert — it's a reconcile, not a new event.
                NotificationHelper.ShowChatNotification(
                    info.ChatId, info.Tag, info.Title, info.Text, info.IconUrl, info.Url,
                    silent: true, messages: info.Messages.IsEmpty ? null : PushMessage.From(info.Messages),
                    senderName: info.SenderName, conversationTitle: info.GroupTitle);
        }

        return Task.CompletedTask;
    }
}
