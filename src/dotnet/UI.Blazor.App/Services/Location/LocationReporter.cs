namespace ActualChat.UI.Blazor.App.Services;

/// <summary>
/// While at least one chat is being shared, runs the platform <see cref="ILocationTracker"/> and, once
/// per <see cref="Constants.Location.UpdatePeriod"/>, reports the current position to each share's
/// <see cref="SharedLocationId"/>. The first fix of a new share posts a chat entry (minting the id);
/// later fixes update only the shared-location record.
/// </summary>
public class LocationReporter(AppUIHub hub) : UIWorkerBase<AppUIHub>(hub), IComputeService
{
    private ILocationTracker Tracker => field ??= Hub.Services.GetRequiredService<ILocationTracker>();
    private LocationUI LocationUI => Hub.LocationUI;
    private Moment ServerNow => Clocks.ServerClock.Now;

    protected override async Task OnRun(CancellationToken cancellationToken)
    {
        var changes = LocationUI.Shares.Computed.Changes(cancellationToken);
        FuncWorker? worker = null;
        await foreach (var cShares in changes.ConfigureAwait(false)) {
            await worker.DisposeSilentlyAsync().ConfigureAwait(false);
            if (cShares.Value.Length == 0) {
                worker = null;
                await Tracker.Stop(cancellationToken).ConfigureAwait(false);
                continue;
            }
            worker = FuncWorker.Start(ct => ReportLoop(cShares.Value, ct), cancellationToken);
        }
    }

    // Private methods

    private async Task ReportLoop(ActiveShare[] activeShares, CancellationToken cancellationToken)
    {
        await Tracker.Start(cancellationToken).ConfigureAwait(false);
        using var cts = cancellationToken.CreateLinkedTokenSource(
            activeShares.Max(x => x.ExpiresAt) - ServerNow);
        // Wait for the first fix before the first cycle: otherwise it runs with an empty LastKnown,
        // reports nothing, and the share only starts a full UpdatePeriod later (the "doesn't start on
        // the first try" bug).
        await Tracker.LastKnown.Computed.When(x => x is not null, cts.Token).ConfigureAwait(false);
        await AsyncChain.From(ReportForChats)
            .Log(LogLevel.Debug, Log)
            .RetryForever(RetryDelaySeq.Exp(0.5, 10), Log)
            .AppendDelay(Constants.Location.UpdatePeriod)
            .CycleForever()
            .RunIsolated(cts.Token)
            .ConfigureAwait(false);
        return;

        Task ReportForChats(CancellationToken cancellationToken1)
            => activeShares.Select(Report).Collect(cancellationToken1);

        async Task Report(ActiveShare share)
        {
            if (share.ExpiresAt <= ServerNow)
                return;

            var point = await Tracker.LastKnown.Use(cancellationToken).ConfigureAwait(false);
            if (point is null)
                return;

            if (share.LocationId is not { } locationId) {
                await PostEntry(share, point).ConfigureAwait(false);
                return;
            }

            var change = Change.Update(new SharedLocationDiff { Point = point });
            var cmd = new SharedLocations_Change(Session, share.ChatId, locationId, change);
            await Commander.Call(cmd, cancellationToken).ConfigureAwait(false);
        }

        async Task PostEntry(ActiveShare share, GeoPoint point)
        {
            var liveDuration = share.ExpiresAt - ServerNow;
            var change = Change.Create(new SharedLocationDiff { Point = point, LiveDuration = liveDuration });
            var shared = await Commander.Call(
                    new SharedLocations_Change(Session, share.ChatId, null, change),
                    cancellationToken)
                .ConfigureAwait(false);
            if (shared is null)
                return;

            var command = new Chats_UpsertEntry(Session, share.ChatId, null) { LocationId = shared.Id };
            await Commander.Call(command, cancellationToken).ConfigureAwait(false);
            LocationUI.SetSharedLocationId(share.ChatId, shared.Id);
        }
    }
}
