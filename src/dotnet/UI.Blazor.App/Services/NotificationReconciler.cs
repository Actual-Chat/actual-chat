using ActualChat.Notifications;
using ActualLab.Resilience;
using Notification = ActualChat.Notifications.Notification;

namespace ActualChat.UI.Blazor.App.Services;

// Client-side safety net: keeps the device's OS notifications in sync with the server's active
// set (INotifications.ListActive).
// - Prune: removes notifications that are no longer active — healing a lost silent-dismissal push
//   or a read on another device. Runs on every active-set change and on foreground resume.
// - Create: re-shows a notification that newly entered the active set but isn't on the device —
//   healing a dropped delivery push. Only newly-added tags are create candidates, so dismissing a
//   banner (which doesn't change the active set) never resurrects it. iOS is prune-only.
public sealed class NotificationReconciler(AppUIHub hub) : UIWorkerBase<AppUIHub>(hub)
{
    private HashSet<string> _lastActiveTags = new();
    private bool _isInitialized;

    private UrlMapper UrlMapper => field ??= Services.UrlMapper();

    protected override Task OnRun(CancellationToken cancellationToken)
    {
        var deviceNotifications = Services.GetService<IDeviceNotifications>();
        if (deviceNotifications is null)
            return Task.CompletedTask; // No notification tray on this platform

        return Task.WhenAll(
            RunChain(ct => ReconcileOnActiveChanges(deviceNotifications, ct), cancellationToken),
            RunChain(ct => ReconcileOnForeground(deviceNotifications, ct), cancellationToken));
    }

    private Task RunChain(Func<CancellationToken, Task> loop, CancellationToken cancellationToken)
        => AsyncChain.From(loop)
            .Log(LogLevel.Debug, Log)
            .RetryForever(RetryDelaySeq.Exp(1, 60), Log)
            .CycleForever()
            .RunIsolated(cancellationToken);

    private async Task ReconcileOnActiveChanges(IDeviceNotifications deviceNotifications, CancellationToken cancellationToken)
    {
        // Reseed on every (re)start: carrying the previous run's tags across a restart would make
        // the first observation look like a diff and re-create banners the user already dismissed.
        _lastActiveTags = new HashSet<string>();
        _isInitialized = false;
        var cActive = await Computed
            .Capture(() => Hub.Notifications.ListActive(Hub.Session, cancellationToken), cancellationToken)
            .ConfigureAwait(false);
        await foreach (var c in cActive.Changes(cancellationToken).ConfigureAwait(false)) {
            if (c.HasError)
                continue;

            var infos = ToInfos(c.Value);
            var currentTags = infos.Select(x => x.Tag).ToHashSet();
            // First observation seeds the baseline without creating, so existing unread chats
            // don't all pop as banners on startup.
            var createTags = _isInitialized
                ? currentTags.Where(t => !_lastActiveTags.Contains(t)).ToList()
                : [];
            _lastActiveTags = currentTags;
            _isInitialized = true;
            await deviceNotifications.Reconcile(infos, createTags, cancellationToken).ConfigureAwait(false);
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
            // On resume only prune (createTags empty): a notification that arrived while we were
            // backgrounded was already create-handled by the active-changes driver (the app stays
            // alive while backgrounded); re-creating here could resurrect a banner read elsewhere.
            if (wasBackground && !isBackground) {
                try {
                    var active = await Hub.Notifications.ListActive(Hub.Session, cancellationToken).ConfigureAwait(false);
                    await deviceNotifications.Reconcile(ToInfos(active), [], cancellationToken).ConfigureAwait(false);
                }
                catch (Exception e) when (!e.IsCancellationOf(cancellationToken)) {
                    // A transient ListActive/reconcile failure on one resume must not kill the loop.
                    Log.LogWarning(e, "Foreground notification reconcile failed");
                }
            }

            wasBackground = isBackground;
        }
    }

    private List<ActiveNotificationInfo> ToInfos(ApiArray<Notification> active)
        => active
            .Select(n => (Tag: n.GetPushTag(), Notification: n))
            .Where(x => x.Tag is not null)
            .Select(x => new ActiveNotificationInfo(
                x.Tag!,
                x.Notification.Title,
                x.Notification.Text,
                x.Notification.IconUrl,
                UrlMapper.ToAbsolute(x.Notification.GetChatLink()),
                (x.Notification as ChatEntryRelatedNotification)?.RecentMessages ?? default,
                x.Notification.GetChatId()))
            .ToList();
}
