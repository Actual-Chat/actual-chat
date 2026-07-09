using ActualChat.Hosting;
using ActualChat.UI.Blazor.Services;

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

        return Task.WhenAll(
            UpdateOnActiveChanges(badge, cancellationToken),
            ReassertOnForeground(badge, cancellationToken));
    }

    private async Task UpdateOnActiveChanges(IAppIconBadge badge, CancellationToken cancellationToken)
    {
        var lastCount = -1;
        var cActive = await Computed
            .Capture(() => Hub.Notifications.ListActive(Hub.Session, cancellationToken), cancellationToken)
            .ConfigureAwait(false);
        await foreach (var c in cActive.Changes(cancellationToken).ConfigureAwait(false)) {
            if (c.HasError)
                continue;

            var count = c.Value.Count;
            if (count == lastCount)
                continue;

            badge.SetBadgeCount(count);
            lastCount = count;
        }
    }

    private async Task ReassertOnForeground(IAppIconBadge badge, CancellationToken cancellationToken)
    {
        // The OS badge drifts while we're backgrounded: a delivered push sets it from aps.badge, and a
        // throttled/dropped silent dismissal push then leaves it stale. ListActive may not change across
        // the resume, so UpdateOnActiveChanges won't fire — re-assert the count unconditionally whenever
        // we return to the foreground.
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
            if (wasBackground && !isBackground) {
                try {
                    // Force a fresh compute (not a possibly-stale cached read) so the resume re-assert
                    // can't write a stale-high count that outlives the OS-drift it exists to correct.
                    var cActive = await Computed
                        .Capture(() => Hub.Notifications.ListActive(Hub.Session, cancellationToken), cancellationToken)
                        .ConfigureAwait(false);
                    cActive = await cActive.Update(cancellationToken).ConfigureAwait(false);
                    if (!cActive.HasError)
                        badge.SetBadgeCount(cActive.Value.Count);
                }
                catch (Exception e) when (!e.IsCancellationOf(cancellationToken)) {
                    // A transient ListActive failure on one resume must not kill the loop.
                    Log.LogWarning(e, "Foreground badge re-assert failed");
                }
            }
            wasBackground = isBackground;
        }
    }
}
