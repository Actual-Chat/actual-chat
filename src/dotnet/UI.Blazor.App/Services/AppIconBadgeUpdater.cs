using ActualLab.Resilience;
using Notification = ActualChat.Notifications.Notification;

namespace ActualChat.UI.Blazor.App.Services;

public interface IAppIconBadge
{
    void SetBadgeCount(int count);
}

public class AppIconBadgeUpdater(AppUIHub hub) : UIWorkerBase<AppUIHub>(hub)
{
    protected override Task OnRun(CancellationToken cancellationToken)
    {
        var badge = Services.GetService<IAppIconBadge>();
        if (badge is null)
            return Task.CompletedTask;

        // Both loops are the badge's only drivers, and on Windows the badge is the only
        // notification surface at all (no tray, no pushes), so a single transient failure used to
        // freeze it - silently, for the rest of the process. A restart re-asserts on its own:
        // lastCount is a local, so the first emission after one always writes.
        return Task.WhenAll(
            RunChain(ct => UpdateOnActiveChanges(badge, ct), cancellationToken),
            RunChain(ct => ReassertOnShown(badge, ct), cancellationToken));
    }

    private Task RunChain(Func<CancellationToken, Task> loop, CancellationToken cancellationToken)
        => AsyncChain.From(loop)
            .Log(LogLevel.Debug, Log)
            .RetryForever(RetryDelaySeq.Exp(1, 60), Log)
            .CycleForever()
            .RunIsolated(cancellationToken);

    private async Task UpdateOnActiveChanges(IAppIconBadge badge, CancellationToken cancellationToken)
    {
        var lastCount = -1;
        var cActive = await Computed
            .Capture(() => Hub.Notifications.ListActive(Hub.Session, cancellationToken), cancellationToken)
            .ConfigureAwait(false);
        await foreach (var c in cActive.Changes(cancellationToken).ConfigureAwait(false)) {
            if (c.HasError)
                continue;

            var count = GetBadgeCount(c.Value);
            if (count == lastCount)
                continue;

            badge.SetBadgeCount(count);
            lastCount = count;
        }
    }

    private async Task ReassertOnShown(IAppIconBadge badge, CancellationToken cancellationToken)
    {
        // The OS badge drifts while we're backgrounded: a delivered push sets it from aps.badge, and a
        // throttled/dropped silent dismissal push then leaves it stale. ListActive may not change across
        // the return, so UpdateOnActiveChanges won't fire — re-assert the count unconditionally whenever
        // the page is shown again.
        var backgroundStateTracker = Services.GetService<BackgroundStateTracker>();
        if (backgroundStateTracker is null)
            return;

        var cIsHidden = await Computed
            .Capture(() => backgroundStateTracker.IsHidden.Use(cancellationToken), cancellationToken)
            .ConfigureAwait(false);
        var wasHidden = cIsHidden.Value;
        await foreach (var c in cIsHidden.Changes(cancellationToken).ConfigureAwait(false)) {
            if (c.HasError)
                continue;

            var isHidden = c.Value;
            if (wasHidden && !isHidden) {
                try {
                    // Force a fresh compute (not a possibly-stale cached read) so the re-assert
                    // can't write a stale-high count that outlives the OS-drift it exists to correct.
                    var cActive = await Computed
                        .Capture(() => Hub.Notifications.ListActive(Hub.Session, cancellationToken), cancellationToken)
                        .ConfigureAwait(false);
                    cActive = await cActive.Update(cancellationToken).ConfigureAwait(false);
                    if (!cActive.HasError)
                        badge.SetBadgeCount(GetBadgeCount(cActive.Value));
                }
                catch (Exception e) when (!e.IsCancellationOf(cancellationToken)) {
                    // A transient ListActive failure on one return must not kill the loop.
                    Log.LogWarning(e, "Badge re-assert on show failed");
                }
            }
            wasHidden = isHidden;
        }
    }

    // Private methods

    private static int GetBadgeCount(ApiArray<Notification> active)
        // A ring is in the active set to be delivered and dismissed, not because anything is
        // unread: counting it takes the badge up for the ring's ~20s and back down after it.
        => active.Where(x => x.Kind != NotificationKind.IncomingCall).Count();
}
