using ActualChat.Kvas;

namespace ActualChat.UI.Blazor.App.Services;

/// <summary>
/// Owns the local user's live-location shares — the device-local list of chats being shared, persisted
/// and resumed on restart — and reports positions: while any chat is shared it runs the platform
/// <see cref="ILocationTracker"/> and reports each share's position once per
/// <see cref="Constants.Location.UpdatePeriod"/>. The first fix mints the share id and posts a chat entry.
/// </summary>
public class LiveLocationReporter : UIWorkerBase<AppUIHub>, IComputeService
{
    private readonly Lock _lock = new();
    private readonly StoredState<ActiveShare[]> _shares;

    private ILocationTracker Tracker => field ??= Hub.Services.GetRequiredService<ILocationTracker>();
    private Moment ServerNow => Clocks.ServerClock.Now;

    public LiveLocationReporter(AppUIHub hub) : base(hub)
        => _shares = StateFactory.NewKvasStored<ActiveShare[]>(
            new (LocalSettings, nameof(ActiveShare)) {
                InitialValue = [],
                Corrector = DropExpired,
                Category = StateCategories.Get(GetType(), nameof(_shares)),
            });

    public Task StartSharing(ChatId chatId, TimeSpan duration, CancellationToken cancellationToken)
    {
        var share = new ActiveShare(chatId, null, ServerNow + duration);
        lock (_lock)
            _shares.Value = _shares.Value.Where(x => x.ChatId != chatId).Append(share).ToArray();
        return Task.CompletedTask;
    }

    public async Task StopSharing(ChatId chatId, CancellationToken cancellationToken)
    {
        // Start/stop is device-local: only the device that started a share can stop it,
        // using the SharedLocationId it persisted in _shares.
        ActiveShare[] stopped;
        lock (_lock) {
            stopped = _shares.Value.Where(x => x.ChatId == chatId).ToArray();
            _shares.Value = _shares.Value.Where(x => x.ChatId != chatId).ToArray();
        }
        foreach (var share in stopped) {
            if (share.LocationId is not { } locationId)
                continue;

            var change = Change.Remove<SharedLocationDiff>();
            var stop = new SharedLocations_Change(Session, chatId, locationId, change);
            await Commander.Call(stop, cancellationToken).ConfigureAwait(false);
        }
    }

    protected override async Task OnRun(CancellationToken cancellationToken)
    {
        // ReSharper disable once InconsistentlySynchronizedField
        var changes = _shares.Computed.Changes(cancellationToken);
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

    private void SetSharedLocationId(ChatId chatId, SharedLocationId locationId)
    {
        lock (_lock)
            _shares.Value = _shares.Value
                .Select(x => x.ChatId == chatId && x.LocationId is null
                    ? x with { LocationId = locationId }
                    : x)
                .ToArray();
    }

    private ValueTask<ActiveShare[]> DropExpired(ActiveShare[] shares, CancellationToken cancellationToken)
        => new (shares.Where(x => x.ExpiresAt > ServerNow).ToArray());

    private async Task ReportLoop(ActiveShare[] activeShares, CancellationToken cancellationToken)
    {
        await Tracker.Start(cancellationToken).ConfigureAwait(false);
        using var cts = cancellationToken.CreateLinkedTokenSource(
            activeShares.Max(x => x.ExpiresAt) - ServerNow);
        await Tracker.LastKnown.Computed.When(x => x is not null, cts.Token).ConfigureAwait(false);
        await AsyncChain.From(ReportForChats)
            .Log(LogLevel.Debug, Log)
            .RetryForever(RetryDelaySeq.Exp(0.5, 10), Log)
            .AppendDelay(Constants.Location.UpdatePeriod)
            .CycleForever()
            .RunIsolated(cts.Token)
            .ConfigureAwait(false);
        return;

        async Task ReportForChats(CancellationToken cancellationToken1) {
            await RestartTrackerIfBroken(cancellationToken1).ConfigureAwait(false);
            await activeShares.Select(Report).Collect(cancellationToken1).ConfigureAwait(false);
        }

        // Some trackers can't self-heal: on a fatal failure (permission denied, or Windows where MAUI
        // shuts the session down) they tear themselves down and report an error instead of recovering.
        // Starting again picks tracking back up once the user resolves the cause; Start returns right
        // away while the tracker is still running, so calling it every cycle is cheap.
        Task RestartTrackerIfBroken(CancellationToken cancellationToken1)
            => Tracker.Error.Value is null
                ? Task.CompletedTask
                : Tracker.Start(cancellationToken1);

        async Task Report(ActiveShare share) {
            if (share.ExpiresAt <= ServerNow)
                return;

            // While tracking is broken, stop re-posting the last known point so recipients'
            // "Updated ..." stops advancing instead of showing a stale fix as fresh.
            if (Tracker.Error.Value is not null)
                return;

            var point = await Tracker.LastKnown.Use(cancellationToken).ConfigureAwait(false);
            if (point is null)
                return;

            var diff = new SharedLocationDiff { Point = point, LiveDuration = share.ExpiresAt - ServerNow };
            var change = Change.Upsert(diff, share.LocationId);
            var shared = await Commander.Call(
                    new SharedLocations_Change(Session, share.ChatId, share.LocationId, change),
                    cancellationToken)
                .ConfigureAwait(false);
            if (shared is null || share.LocationId is not null)
                return;

            var command = new Chats_UpsertEntry(Session, share.ChatId, null) { LocationId = shared.Id };
            await Commander.Call(command, cancellationToken).ConfigureAwait(false);
            SetSharedLocationId(share.ChatId, shared.Id);
        }
    }
}
