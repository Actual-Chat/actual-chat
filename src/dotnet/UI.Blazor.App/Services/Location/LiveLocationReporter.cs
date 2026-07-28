using ActualChat.Kvas;
using ActualChat.UI.Blazor.Services;

namespace ActualChat.UI.Blazor.App.Services;

/// <summary>
/// Owns the local user's live-location shares — the device-local list of chats being shared, persisted
/// and resumed on restart — and reports positions: while any chat is shared it runs the platform
/// <see cref="ILocationTracker"/> and reports each share's position once per
/// <see cref="Constants.Location.UpdatePeriod"/>. The first fix mints the share id and posts a chat entry.
/// </summary>
public class LiveLocationReporter : UIWorkerBase<AppUIHub>, IComputeService
{
    private static readonly TimeSpan TroubleshooterDelay = TimeSpan.FromSeconds(7.5);
    // CancellationTokenSource.CancelAfter rejects delays over ~49.7 days
    private static readonly TimeSpan MaxReportLoopTimeout = TimeSpan.FromDays(49);

    private readonly Lock _lock = new();
    private readonly StoredState<ActiveShare[]> _shares;

    private ILocationTracker Tracker => field ??= Hub.Services.GetRequiredService<ILocationTracker>();
    private LocationPermissionHandler LocationPermission
        => field ??= Hub.Services.GetRequiredService<LocationPermissionHandler>();
    private Moment ServerNow => Clocks.ServerClock.Now;

    public LiveLocationReporter(AppUIHub hub) : base(hub)
        => _shares = StateFactory.NewKvasStored<ActiveShare[]>(
            new (LocalSettings, nameof(ActiveShare)) {
                InitialValue = [],
                Corrector = DropExpired,
                Category = StateCategories.Get(GetType(), nameof(_shares)),
            });

    public void StartSharing(ChatId chatId, TimeSpan duration)
    {
        var share = new ActiveShare(chatId, null, ServerNow, duration);
        lock (_lock)
            _shares.Value = [.. _shares.Value.Where(x => x.ChatId != chatId), share];
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

    protected override Task OnRun(CancellationToken cancellationToken)
    {
        var retryDelays = RetryDelaySeq.Exp(0.5, 10);
        return (
            from chain in new[] {
                AsyncChain.From(DispatchShares),
                AsyncChain.From(TroubleshootTracking),
            }
            select chain
                .Log(LogLevel.Debug, Log)
                .RetryForever(retryDelays, Log)
            ).RunIsolated(cancellationToken);
    }

    // Protected/internal methods

    [ComputeMethod]
    protected virtual async Task<bool> MustTroubleshoot(CancellationToken cancellationToken)
    {
        // ReSharper disable once InconsistentlySynchronizedField
        var shares = await _shares.Use(cancellationToken).ConfigureAwait(false);
        if (shares.Length == 0)
            return false;

        return await Tracker.Error.Use(cancellationToken).ConfigureAwait(false) is GeoTrackingError.PermissionDenied;
    }

    // Private methods

    private async Task DispatchShares(CancellationToken cancellationToken)
    {
        // Runs one ReportLoop worker for the current shares, replacing it whenever the set changes.
        // ReSharper disable once InconsistentlySynchronizedField
        var changes = _shares.Computed.Changes(cancellationToken);
        FuncWorker? worker = null;
        try {
            await foreach (var cShares in changes.ConfigureAwait(false)) {
                await worker.DisposeSilentlyAsync().ConfigureAwait(false);
                worker = null;
                if (cShares.Value.Length == 0) {
                    await Tracker.Stop(cancellationToken).ConfigureAwait(false);
                    continue;
                }

                worker = FuncWorker.Start(ct => ReportLoop(cShares.Value, ct), cancellationToken);
            }
        }
        finally {
            await worker.DisposeSilentlyAsync().ConfigureAwait(false);
        }
    }

    private async Task TroubleshootTracking(CancellationToken cancellationToken)
    {
        var wasRequired = false;
        FuncWorker? troubleshooter = null;
        try {
            var cRequired = await Computed
                .Capture(() => MustTroubleshoot(cancellationToken), cancellationToken)
                .ConfigureAwait(false);
            await foreach (var (isRequired, _) in cRequired.Changes(cancellationToken).ConfigureAwait(false)) {
                if (isRequired == wasRequired)
                    continue;

                wasRequired = isRequired;
                await troubleshooter.DisposeSilentlyAsync().ConfigureAwait(false);
                troubleshooter = null;
                if (!isRequired)
                    continue;

                // Permission was revoked mid-share: invalidate the cached grant the same way
                // AudioRecorder does for the mic, so the next CheckOrRequest re-detects it.
                LocationPermission.ForgetCached();
                troubleshooter = FuncWorker.Start(ShowLocationTroubleshooter, cancellationToken);
            }
        }
        finally {
            await troubleshooter.DisposeSilentlyAsync().ConfigureAwait(false);
        }
    }

    private async Task ShowLocationTroubleshooter(CancellationToken cancellationToken)
    {
        await Clocks.CpuClock.Delay(TroubleshooterDelay, cancellationToken).ConfigureAwait(false);
        await Dispatcher.InvokeAsync(async () => {
            var modalRef = await ModalUI
                .Show(new LocationTroubleshooterModal.Model(), cancellationToken)
                .ConfigureAwait(true);
            try {
                await modalRef.WhenClosed.WaitAsync(cancellationToken).ConfigureAwait(true);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) {
                modalRef.Close();
            }
        }).ConfigureAwait(false);
    }

    private ValueTask<ActiveShare[]> DropExpired(ActiveShare[] shares, CancellationToken cancellationToken)
        => new (shares.Where(x => x.ExpiresAt > ServerNow).ToArray());

    private async Task ReportLoop(ActiveShare[] activeShares, CancellationToken cancellationToken)
    {
        activeShares = await InitializeShares(activeShares, cancellationToken).ConfigureAwait(false);
        await Tracker.Start(cancellationToken).ConfigureAwait(false);
        var timeout = activeShares.Max(x => x.ExpiresAt) - ServerNow;
        using var cts = cancellationToken.CreateLinkedTokenSource(timeout < MaxReportLoopTimeout ? timeout : null);
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
            await activeShares.Select(x => Report(x, cancellationToken1))
                .Collect(cancellationToken1)
                .ConfigureAwait(false);
        }
    }

    // Some trackers can't self-heal: on a fatal failure (permission denied, or Windows where MAUI
    // shuts the session down) they tear themselves down and report an error instead of recovering.
    // Starting again picks tracking back up once the user resolves the cause; Start returns right
    // away while the tracker is still running, so calling it every cycle is cheap.
    private Task RestartTrackerIfBroken(CancellationToken cancellationToken)
        => Tracker.Error.Value is null
            ? Task.CompletedTask
            : Tracker.Start(cancellationToken);

    private async Task Report(ActiveShare share, CancellationToken cancellationToken) {
        if (share.ExpiresAt <= ServerNow)
            return;

        // While tracking is broken, stop re-posting the last known point so recipients'
        // "Updated ..." stops advancing instead of showing a stale fix as fresh.
        if (Tracker.Error.Value is not null)
            return;

        var point = await Tracker.LastKnown.Use(cancellationToken).ConfigureAwait(false);
        if (point is null)
            return;

        await InitializeShare(share, point, cancellationToken).ConfigureAwait(false);
        var diff = new SharedLocationDiff { Point = point, LiveDuration = share.Duration };
        var change = Change.Upsert(diff, share.LocationId);
        await Commander.Call(
                new SharedLocations_Change(Session, share.ChatId, share.LocationId, change),
                cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task<ActiveShare[]> InitializeShares(ActiveShare[] activeShares, CancellationToken cancellationToken) {
        if (activeShares.All(x => x.LocationId is not null))
            return activeShares;

        var point = await Tracker.Get(false, cancellationToken).ConfigureAwait(false);
        if (point is null)
            return activeShares;

        return await activeShares.Select(x => InitializeShare(x, point, cancellationToken)).Collect(cancellationToken).ConfigureAwait(false);
    }

    private async Task<ActiveShare> InitializeShare(
        ActiveShare share, GeoPoint point, CancellationToken cancellationToken1) {
        if (share.LocationId is not null)
            return share;

        var diff = new SharedLocationDiff { Point = point, LiveDuration = share.Duration };
        var change = Change.Create(diff);
        var sharedLocation = await Commander.Call(
                new SharedLocations_Change(Session, share.ChatId, null, change),
                cancellationToken1)
            .ConfigureAwait(false);
        if (sharedLocation is null)
            return share;

        var command = new Chats_UpsertEntry(Session, share.ChatId, null) { LocationId = sharedLocation.Id };
        await Commander.Call(command, cancellationToken1).ConfigureAwait(false);
        SetSharedLocationId(share.ChatId, sharedLocation.Id);
        return share with { LocationId = sharedLocation.Id };
    }

    private void SetSharedLocationId(ChatId chatId, SharedLocationId locationId)
    {
        lock (_lock)
            _shares.Value = _shares.Value
                .Select(x => x.ChatId == chatId && x.LocationId is null
                    ? x with { LocationId = locationId }
                    : x)
                .ToArray();
    }
}
