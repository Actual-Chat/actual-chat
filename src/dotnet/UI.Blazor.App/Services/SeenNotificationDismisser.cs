using ActualChat.Notifications;
using ActualLab.Resilience;
using Notification = ActualChat.Notifications.Notification;

namespace ActualChat.UI.Blazor.App.Services;

// Clears NotificationDismissMode.OnView notifications - reactions and attention pings - once the
// entry they point at has actually been on screen. Their anchor entry is one the Read position
// may already cover (the recipient's own message, a mention they saw), so it can't answer "have
// you seen this".
public sealed class SeenNotificationDismisser(AppUIHub hub) : UIWorkerBase<AppUIHub>(hub)
{
    private readonly Dictionary<NotificationId, Moment> _dismissedSentAt = new();

    protected override Task OnRun(CancellationToken cancellationToken)
        => Task.WhenAll(
            RunChain(DismissOnVisibilityChanges, cancellationToken),
            RunChain(DismissOnActiveChanges, cancellationToken));

    private Task RunChain(Func<CancellationToken, Task> loop, CancellationToken cancellationToken)
        => AsyncChain.From(loop)
            .Log(LogLevel.Debug, Log)
            .RetryForever(RetryDelaySeq.Exp(1, 60), Log)
            .CycleForever()
            .RunIsolated(cancellationToken);

    private async Task DismissOnVisibilityChanges(CancellationToken cancellationToken)
    {
        var cVisibility = await Computed
            .Capture(() => Hub.ChatUI.ItemVisibility.Use(cancellationToken), cancellationToken)
            .ConfigureAwait(false);
        await foreach (var c in cVisibility.Changes(cancellationToken).ConfigureAwait(false)) {
            if (c.HasError)
                continue;

            var active = await Hub.Notifications.ListActive(Session, cancellationToken).ConfigureAwait(false);
            await DismissSeen(active, c.Value, cancellationToken).ConfigureAwait(false);
        }
    }

    // A reaction to an entry that's already on screen moves nothing the visibility state depends on,
    // so on its own the visibility loop would leave it up until its lifespan runs out.
    private async Task DismissOnActiveChanges(CancellationToken cancellationToken)
    {
        var cActive = await Computed
            .Capture(() => Hub.Notifications.ListActive(Session, cancellationToken), cancellationToken)
            .ConfigureAwait(false);
        await foreach (var c in cActive.Changes(cancellationToken).ConfigureAwait(false)) {
            if (c.HasError)
                continue;

            await DismissSeen(c.Value, Hub.ChatUI.ItemVisibility.LastNonErrorValue, cancellationToken)
                .ConfigureAwait(false);
        }
    }

    private async Task DismissSeen(
        ApiArray<Notification> active,
        ChatViewItemVisibility visibility,
        CancellationToken cancellationToken)
    {
        if (visibility.IsEmpty)
            return;

        foreach (var notification in active) {
            if (notification.DismissMode != NotificationDismissMode.OnView || !IsSeen(notification, visibility))
                continue;

            await Dismiss(notification, cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task Dismiss(Notification notification, CancellationToken cancellationToken)
    {
        // ListActive lags the dismissal, so the same notification would otherwise be re-dismissed
        // on every visibility change until it drops out. Keyed by SentAt rather than by id alone:
        // a reaction's id is (user, entry), so the next reaction to the same entry reuses it and
        // has to be dismissable again.
        var id = notification.Id;
        var sentAt = notification.SentAt;
        lock (Lock) {
            if (_dismissedSentAt.TryGetValue(id, out var dismissedSentAt) && dismissedSentAt >= sentAt)
                return;

            _dismissedSentAt[id] = sentAt;
        }
        try {
            var command = new Notifications_Dismiss { Session = Session, NotificationId = id };
            await Commander.Call(command, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception e) when (!e.IsCancellationOf(cancellationToken)) {
            lock (Lock) {
                if (_dismissedSentAt.TryGetValue(id, out var dismissedSentAt) && dismissedSentAt == sentAt)
                    _dismissedSentAt.Remove(id);
            }
            Log.LogWarning(e, "Failed to dismiss seen notification {NotificationId}", id);
        }
    }

    private static bool IsSeen(Notification notification, ChatViewItemVisibility visibility)
        => notification is ChatEntryNotification entry
            && entry.ChatId == visibility.ChatId
            && visibility.VisibleMessageLids.Contains(entry.EntryLid);
}
