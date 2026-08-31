using AndroidX.Core.App;
using ActualChat.Notifications;
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
        var activeTags = active.Select(x => x.Tag).ToHashSet();
        var notificationManager = NotificationManagerCompat.From(Android.App.Application.Context);

        var shownTags = new HashSet<string>();
        var shown = notificationManager?.ActiveNotifications;
        if (shown != null)
            foreach (var statusBarNotification in shown) {
                var tag = statusBarNotification.Tag;
                if (tag.IsNullOrEmpty())
                    continue;
                if (!activeTags.Contains(tag))
                    notificationManager?.Cancel(tag, statusBarNotification.Id);
                else
                    shownTags.Add(tag);
            }

        foreach (var tag in createTags) {
            if (shownTags.Contains(tag))
                continue;
            var info = active.FirstOrDefault(x => x.Tag == tag);
            if (info == null)
                continue;

            if (IncomingCallNotifications.TryParseCallTag(tag) is { } callChatId)
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
