using ActualChat.Notifications;
using ActualLab.Resilience;
using Notification = ActualChat.Notifications.Notification;

namespace ActualChat.UI.Blazor.App.Services;

// Clears NotificationDismissMode.OnView notifications - reactions - once the entry they point at
// has actually been on screen. Their anchor entry is the recipient's own message, so the Read
// position can't answer "have you seen this": it covers that entry from the moment it was sent.
public class SeenNotificationDismisser(AppUIHub hub) : UIWorkerBase<AppUIHub>(hub)
{
    private readonly HashSet<NotificationId> _dismissed = [];

    protected override Task OnRun(CancellationToken cancellationToken)
        => AsyncChain.From(DismissSeen)
            .Log(LogLevel.Debug, Log)
            .RetryForever(RetryDelaySeq.Exp(1, 60), Log)
            .CycleForever()
            .RunIsolated(cancellationToken);

    private async Task DismissSeen(CancellationToken cancellationToken)
    {
        var cVisibility = await Computed
            .Capture(() => Hub.ChatUI.ItemVisibility.Use(cancellationToken), cancellationToken)
            .ConfigureAwait(false);
        await foreach (var c in cVisibility.Changes(cancellationToken).ConfigureAwait(false)) {
            if (c.HasError)
                continue;

            var visibility = c.Value;
            if (visibility.IsEmpty)
                continue;

            var active = await Hub.Notifications.ListActive(Session, cancellationToken).ConfigureAwait(false);
            var seen = active
                .Where(n => n.DismissMode == NotificationDismissMode.OnView && IsSeen(n, visibility))
                .ToList();
            foreach (var notification in seen)
                await Dismiss(notification, cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task Dismiss(Notification notification, CancellationToken cancellationToken)
    {
        // ListActive lags the dismissal, so the same notification would otherwise be re-dismissed
        // on every visibility change until it drops out.
        lock (Lock) {
            if (!_dismissed.Add(notification.Id))
                return;
        }
        try {
            await Commander.Call(new Notifications_Dismiss { Session = Session, NotificationId = notification.Id }, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception e) when (!e.IsCancellationOf(cancellationToken)) {
            lock (Lock)
                _dismissed.Remove(notification.Id);
            Log.LogWarning(e, "Failed to dismiss seen notification {NotificationId}", notification.Id);
        }
    }

    private static bool IsSeen(Notification notification, ChatViewItemVisibility visibility)
        => notification is ChatEntryNotification entry
            && entry.ChatId == visibility.ChatId
            && visibility.VisibleMessageLids.Contains(entry.EntryLid);
}
