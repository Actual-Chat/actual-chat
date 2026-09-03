using ActualChat.UI.Blazor.App.Services;
using Foundation;
using UserNotifications;

namespace ActualChat.App.Maui;

// macOS IDeviceNotifications: full create + prune. The AppKit backend has no push path, so
// "create" IS delivery here: NotificationReconciler pulls the active set over the app's own
// RPC and every newly-active notification becomes a local UNNotificationRequest - which also
// means notifications appear only while the app is running, by design.
public class MacOSDeviceNotifications : IDeviceNotifications
{
    private static readonly ILogger Log = StaticLog.For<MacOSDeviceNotifications>();

    private readonly SemaphoreSlim _reconcileLock = new(1, 1);
    // Last observed content per active tag; a change means a message the user wasn't alerted
    // about yet, which re-posts the banner the way a push would - even one they dismissed.
    // Seeded silently on first observation, like the reconciler's own createTags baseline.
    private readonly Dictionary<string, (string Title, string Text)> _lastContentByTag = new();

    private static UNUserNotificationCenter NotificationCenter => UNUserNotificationCenter.Current;

    public async Task Reconcile(
        IReadOnlyList<ActiveNotificationInfo> active,
        IReadOnlyCollection<string> createTags,
        CancellationToken cancellationToken)
    {
        await _reconcileLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try {
            await ReconcileUnsafe(active, createTags).ConfigureAwait(false);
        }
        finally {
            _reconcileLock.Release();
        }
    }

    // Private methods

    private async Task ReconcileUnsafe(
        IReadOnlyList<ActiveNotificationInfo> active,
        IReadOnlyCollection<string> createTags)
    {
        var activeTags = active.Select(x => x.Tag).ToHashSet();
        var delivered = await NotificationCenter.GetDeliveredNotificationsAsync().ConfigureAwait(false);

        var shownByTag = new Dictionary<string, UNNotification>();
        var toRemove = new List<string>();
        foreach (var notification in delivered) {
            var tag = notification.Request.Content.ThreadIdentifier;
            if (tag.IsNullOrEmpty())
                continue;
            if (!activeTags.Contains(tag))
                toRemove.Add(notification.Request.Identifier);
            else
                shownByTag[tag] = notification;
        }
        if (toRemove.Count > 0)
            NotificationCenter.RemoveDeliveredNotifications(toRemove.ToArray());
        Log.LogDebug(
            "Reconcile: {ActiveCount} active, {CreateCount} to create, {ShownCount} shown, {RemovedCount} removed",
            active.Count, createTags.Count, shownByTag.Count, toRemove.Count);

        foreach (var info in active) {
            var isChanged = _lastContentByTag.TryGetValue(info.Tag, out var last)
                && (last.Title != info.Title || last.Text != info.Text);
            var isNew = createTags.Contains(info.Tag) && !shownByTag.ContainsKey(info.Tag);
            _lastContentByTag[info.Tag] = (info.Title, info.Text);
            if (!isNew && !isChanged)
                continue;

            await Post(info).ConfigureAwait(false);
            Log.LogDebug("Reconcile: {Action} notification for tag {Tag}", isChanged ? "replaced" : "created", info.Tag);
        }

        foreach (var staleTag in _lastContentByTag.Keys.Where(t => !activeTags.Contains(t)).ToList())
            _lastContentByTag.Remove(staleTag);

        return;

        static async Task Post(ActiveNotificationInfo info) {
            using var content = new UNMutableNotificationContent {
                Title = info.Title,
                Body = info.Text,
                ThreadIdentifier = info.Tag,
                // Unlike the other platforms, this is delivery rather than healing a dropped
                // push, so it alerts.
                Sound = UNNotificationSound.Default,
                UserInfo = NSDictionary.FromObjectAndKey(
                    new NSString(info.Url),
                    new NSString(Constants.Notification.MessageDataKeys.Link)),
            };
            // The tag doubles as the identifier, so a re-posted tag replaces its predecessor
            var request = UNNotificationRequest.FromIdentifier(info.Tag, content, null);
            await NotificationCenter.AddNotificationRequestAsync(request).ConfigureAwait(false);
        }
    }
}
