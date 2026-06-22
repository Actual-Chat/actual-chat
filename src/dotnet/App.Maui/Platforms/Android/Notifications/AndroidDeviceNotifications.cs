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
        var notificationManager = NotificationManagerCompat.From(Android.App.Application.Context);

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
                NotificationHelper.ShowChatNotification(info.Tag, info.Title, info.Text, info.IconUrl, info.Url);
        }
        return Task.CompletedTask;
    }
}
