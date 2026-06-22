using ActualChat.Hosting;
using ActualChat.Notifications;
using Notification = ActualChat.Notifications.Notification;

namespace ActualChat.UI.Blazor.App.Services;

// Client-side safety net: keeps the device's OS notifications in sync with the server's active
// set (INotifications.ListActive). Reactively prunes notifications that are no longer active —
// healing a lost silent-dismissal push or a read on another device — both when the active set
// changes and whenever the app returns to the foreground (where a missed dismissal can surface).
public class NotificationReconciler(AppUIHub hub) : UIWorkerBase<AppUIHub>(hub)
{
    protected override Task OnRun(CancellationToken cancellationToken)
    {
        var deviceNotifications = Services.GetService<IDeviceNotifications>();
        if (deviceNotifications is null)
            return Task.CompletedTask; // No notification tray on this platform

        return Task.WhenAll(
            ReconcileOnActiveChanges(deviceNotifications, cancellationToken),
            ReconcileOnForeground(deviceNotifications, cancellationToken));
    }

    private async Task ReconcileOnActiveChanges(IDeviceNotifications deviceNotifications, CancellationToken cancellationToken)
    {
        var cActive = await Computed
            .Capture(() => Hub.Notifications.ListActive(Hub.Session, cancellationToken), cancellationToken)
            .ConfigureAwait(false);
        await foreach (var c in cActive.Changes(cancellationToken).ConfigureAwait(false)) {
            if (c.HasError)
                continue;
            await Reconcile(deviceNotifications, c.Value, cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task ReconcileOnForeground(IDeviceNotifications deviceNotifications, CancellationToken cancellationToken)
    {
        var backgroundStateTracker = Services.GetService<BackgroundStateTracker>();
        if (backgroundStateTracker is null)
            return;

        var cIsBackground = await Computed
            .Capture(() => backgroundStateTracker.IsBackground.Use(cancellationToken), cancellationToken)
            .ConfigureAwait(false);
        var wasBackground = cIsBackground.Value;
        await foreach (var c in cIsBackground.Changes(cancellationToken).ConfigureAwait(false)) {
            if (c.HasError)
                continue;

            var isBackground = c.Value;
            // Re-prune on resume even if the active set is unchanged: a dismissal push may have
            // been dropped while we were backgrounded.
            if (wasBackground && !isBackground) {
                var active = await Hub.Notifications.ListActive(Hub.Session, cancellationToken).ConfigureAwait(false);
                await Reconcile(deviceNotifications, active, cancellationToken).ConfigureAwait(false);
            }
            wasBackground = isBackground;
        }
    }

    private static Task Reconcile(
        IDeviceNotifications deviceNotifications, ApiArray<Notification> active, CancellationToken cancellationToken)
    {
        var infos = active
            .Select(n => (Tag: n.GetChatTag(), Notification: n))
            .Where(x => x.Tag is not null)
            .Select(x => new ActiveNotificationInfo(x.Tag!, x.Notification.Title, x.Notification.Text, x.Notification.IconUrl, ""))
            .ToList();
        // Prune is idempotent, so overlapping runs from the two drivers are harmless.
        return deviceNotifications.Reconcile(infos, cancellationToken);
    }
}
