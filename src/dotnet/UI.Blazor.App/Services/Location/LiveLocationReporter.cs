using ActualChat.Kvas;
using ActualChat.Localization;
using ActualChat.UI.Blazor.App.Components;
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
    private static readonly TimeSpan StoppedSharesSweepTimeout = TimeSpan.FromSeconds(5);
    // CancellationTokenSource.CancelAfter rejects delays over ~49.7 days
    private static readonly TimeSpan MaxReportLoopTimeout = TimeSpan.FromDays(49);

    private readonly Lock _lock = new();
    private readonly StoredState<ActiveShare[]> _shares;

    private ILocationTracker Tracker => field ??= Hub.Services.GetRequiredService<ILocationTracker>();
    private LocationPermissionHandler LocationPermission
        => field ??= Hub.Services.GetRequiredService<LocationPermissionHandler>();
    private ISharedLocations SharedLocations => Hub.SharedLocations;
    private Moment ServerNow => Clocks.ServerClock.Now;

    public LiveLocationReporter(AppUIHub hub) : base(hub)
        => _shares = StateFactory.NewKvasStored<ActiveShare[]>(
            new (LocalSettings, nameof(ActiveShare)) {
                InitialValue = [],
                Corrector = DropExpired,
                Category = StateCategories.Get(GetType(), nameof(_shares)),
            });

    [ComputeMethod]
    public virtual async Task<ImmutableArray<ChatId>> GetActiveShareChatIds(CancellationToken cancellationToken)
    {
        // ReSharper disable once InconsistentlySynchronizedField
        var shares = await _shares.Use(cancellationToken).ConfigureAwait(false);
        return shares.Select(x => x.ChatId).Distinct().ToImmutableArray();
    }

    [ComputeMethod]
    public virtual async Task<ActiveShare?> GetActiveShare(ChatId chatId, CancellationToken cancellationToken)
    {
        // ReSharper disable once InconsistentlySynchronizedField
        var shares = await _shares.Use(cancellationToken).ConfigureAwait(false);
        var now = ServerNow;
        var share = shares.FirstOrDefault(x => x.ChatId == chatId && x.ExpiresAt > now);
        if (share is not null && share.ExpiresAt != Moment.MaxValue)
            Computed.GetCurrent().Invalidate(share.ExpiresAt - now);
        return share;
    }

    public void StartSharing(ChatId chatId, TimeSpan duration)
    {
        // The replaced shares' server rows must be stopped too — dropping them from _shares
        // orphans the rows otherwise: their ids live only here, so nothing else can end them.
        var share = new ActiveShare(chatId, null, ServerNow, duration);
        ActiveShare[] replaced;
        lock (_lock) {
            replaced = [.. _shares.Value.Where(x => x.ChatId == chatId)];
            _shares.Value = [.. _shares.Value.Where(x => x.ChatId != chatId), share];
        }
        _ = StopServerShares(replaced, StopToken);
    }

    public async Task StopSharing(ChatId chatId, SharedLocationId? locationId, CancellationToken cancellationToken)
    {
        // This device's shares in the chat are stopped either way; locationId names the author's live share
        // when another device owns it, which _shares knows nothing about.
        ActiveShare[] stopped;
        lock (_lock) {
            stopped = _shares.Value.Where(x => x.ChatId == chatId).ToArray();
            _shares.Value = _shares.Value.Where(x => x.ChatId != chatId).ToArray();
        }
        await StopServerShares(stopped, cancellationToken).ConfigureAwait(false);
        if (locationId is { } id && stopped.All(x => x.LocationId != id))
            await StopServerShare(chatId, id, cancellationToken).ConfigureAwait(false);
    }

    public async Task StopAllSharing(CancellationToken cancellationToken)
    {
        ActiveShare[] stopped;
        lock (_lock) {
            stopped = _shares.Value;
            _shares.Value = [];
        }
        await StopServerShares(stopped, cancellationToken).ConfigureAwait(false);
    }

    protected override Task OnRun(CancellationToken cancellationToken)
    {
        var retryDelays = RetryDelaySeq.Exp(0.5, 10);
        return (
            from chain in new[] {
                AsyncChain.From(DispatchShares),
                AsyncChain.From(WatchStoppedShares),
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

    [ComputeMethod]
    protected virtual async Task<ImmutableArray<SharedLocationId>> ListStoppedShares(
        CancellationToken cancellationToken)
    {
        // Reads Get(myId) rather than ListLive(chatId): the latter invalidates on every sharer's fix, forever,
        // while Get goes quiet once the share is frozen - and a share missing from ListLive is ambiguous,
        // whereas a share that reads back as not live is a definite server answer.
        // ReSharper disable once InconsistentlySynchronizedField
        var shares = await _shares.Use(cancellationToken).ConfigureAwait(false);
        var now = ServerNow;
        var result = ImmutableArray<SharedLocationId>.Empty;
        foreach (var share in shares) {
            if (share.LocationId is not { } id || share.ExpiresAt <= now)
                continue;

            // A null read is "I don't know" - offline, or not cached yet - and must not stop a live share.
            var location = await SharedLocations.Get(Session, share.ChatId, id, cancellationToken)
                .ConfigureAwait(false);
            if (location is not null && !location.IsLive(now))
                result = result.Add(id);
        }
        return result;
    }

    // Private methods

    private async Task StopServerShares(ActiveShare[] shares, CancellationToken cancellationToken)
    {
        foreach (var share in shares)
            if (share.LocationId is { } locationId)
                await StopServerShare(share.ChatId, locationId, cancellationToken).ConfigureAwait(false);
    }

    private Task StopServerShare(ChatId chatId, SharedLocationId locationId, CancellationToken cancellationToken)
    {
        var stop = new SharedLocations_Change {
            Session = Session,
            ChatId = chatId,
            Id = locationId,
            Change = Change.Remove<SharedLocationDiff>(),
        };
        return Commander.Call(stop, cancellationToken);
    }

    private async Task DispatchShares(CancellationToken cancellationToken)
    {
        // Runs one ReportLoop worker for the current shares, replacing it whenever the set changes.
        // ReSharper disable once InconsistentlySynchronizedField
        var changes = _shares.Computed.Changes(cancellationToken)
            .AdjacentDistinctBy(x => x.Value, ActiveSharesComparer.Instance);
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

    private async Task WatchStoppedShares(CancellationToken cancellationToken)
    {
        var cStopped = await Computed
            .Capture(() => ListStoppedShares(cancellationToken), cancellationToken)
            .ConfigureAwait(false);
        await foreach (var (stoppedIds, _) in cStopped.Changes(cancellationToken).ConfigureAwait(false))
            if (!stoppedIds.IsDefaultOrEmpty)
                await DropStoppedShares(stoppedIds, cancellationToken).ConfigureAwait(false);
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
                .Show(new PermissionGuideModal.Model(PermissionKind.Location), cancellationToken)
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
        => new (WithoutExpired(shares));

    private async Task ReportLoop(ActiveShare[] activeShares, CancellationToken cancellationToken)
    {
        activeShares = await SweepStoppedShares(activeShares, cancellationToken).ConfigureAwait(false);
        if (activeShares.Length == 0)
            return;

        try {
            // Post entries upfront so their visibility doesn't wait for the first live fix;
            // on failure ReportForChats picks the pending shares up with retries.
            activeShares = await InitializeShares(activeShares, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception e) when (!e.IsCancellationOf(cancellationToken)) {
            Log.LogWarning(e, "Failed to initialize shares upfront");
        }
        await Tracker.Start(cancellationToken).ConfigureAwait(false);
        var timeout = activeShares.Max(x => x.ExpiresAt) - ServerNow;
        using var cts = cancellationToken.CreateLinkedTokenSource(timeout < MaxReportLoopTimeout ? timeout : null);
        try {
            // Waits for the first live fix, so the cycle below doesn't re-post the cached one as fresh.
            await Tracker.Get(true, cts.Token).ConfigureAwait(false);
            await AsyncChain.From(ReportForChats)
                .Log(LogLevel.Debug, Log)
                .RetryForever(RetryDelaySeq.Exp(0.5, 10), Log)
                .AppendDelay(Constants.Location.UpdatePeriod)
                .CycleForever()
                .RunIsolated(cts.Token)
                .ConfigureAwait(false);
        }
        finally {
            // cts fired on its own = the last share expired. Nothing re-reads _shares on a timer, so
            // without this DispatchShares never sees the change - and the tracker (on Android a
            // foreground service that outlives this loop) would run on until the next launch.
            if (!cancellationToken.IsCancellationRequested)
                DropExpiredShares();
        }
        return;

        async Task ReportForChats(CancellationToken cancellationToken1) {
            await RestartTrackerIfBroken(cancellationToken1).ConfigureAwait(false);
            // The comparer suppresses ReportLoop restarts on LocationId-only changes,
            // so the local list is the source of truth for what's already initialized
            activeShares = await InitializeShares(activeShares, cancellationToken1).ConfigureAwait(false);
            await activeShares.Select(x => Report(x, cancellationToken1))
                .Collect(cancellationToken1)
                .ConfigureAwait(false);
        }
    }

    private async Task<ActiveShare[]> SweepStoppedShares(
        ActiveShare[] activeShares,
        CancellationToken cancellationToken)
    {
        // A device relaunching into a share taken over while it was away must not spin GPS and the
        // foreground service up for it. Fail-open: no answer within the timeout keeps the share.
        try {
            using var cts = cancellationToken.CreateLinkedTokenSource(StoppedSharesSweepTimeout);
            var stoppedIds = await ListStoppedShares(cts.Token).ConfigureAwait(false);
            if (stoppedIds.IsDefaultOrEmpty)
                return activeShares;

            await DropStoppedShares(stoppedIds, cancellationToken).ConfigureAwait(false);
            return activeShares.Where(x => x.LocationId is not { } id || !stoppedIds.Contains(id)).ToArray();
        }
        catch (Exception e) when (!e.IsCancellationOf(cancellationToken)) {
            Log.LogWarning(e, "Failed to check shares against the server on start");
            return activeShares;
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

    private async Task Report(ActiveShare share, CancellationToken cancellationToken)
    {
        if (share.ExpiresAt <= ServerNow)
            return;

        // While tracking is broken, stop re-posting the last known point so recipients'
        // "Updated ..." stops advancing instead of showing a stale fix as fresh.
        if (Tracker.Error.Value is not null)
            return;

        if (await Tracker.Get(false, cancellationToken).ConfigureAwait(false) is not { Point: var point })
            return;

        // Not initialized yet (InitializeShares failed to get a point or to post); next cycle retries
        if (share.LocationId is not { } locationId)
            return;

        var diff = new SharedLocationDiff { Point = point, LiveDuration = share.Duration };
        var change = Change.Update(diff);
        var location = await Commander.Call(
                new SharedLocations_Change {
                    Session = Session,
                    ChatId = share.ChatId,
                    Id = locationId,
                    Change = change,
                },
                cancellationToken)
            .ConfigureAwait(false);
        // A push into a frozen share is answered with that share - the fallback for a device that missed
        // the takeover's invalidation.
        if (location is not null && !location.IsLive(ServerNow))
            await DropStoppedShares([locationId], cancellationToken).ConfigureAwait(false);
    }

    private async Task DropStoppedShares(
        ImmutableArray<SharedLocationId> stoppedIds,
        CancellationToken cancellationToken)
    {
        ActiveShare[] dropped;
        lock (_lock) {
            dropped = _shares.Value.Where(x => x.LocationId is { } id && stoppedIds.Contains(id)).ToArray();
            if (dropped.Length > 0)
                _shares.Value = _shares.Value.Except(dropped).ToArray();
        }
        foreach (var share in dropped)
            await NotifyShareDropped(share.ChatId, cancellationToken).ConfigureAwait(false);
    }

    private async Task NotifyShareDropped(ChatId chatId, CancellationToken cancellationToken)
    {
        // The share neither expired nor was stopped here, so the user is told; whether the author is
        // live from elsewhere now decides between "moved" and "stopped".
        var ownLive = await Hub.LocationUI.GetOwnLive(chatId, cancellationToken).ConfigureAwait(false);
        var text = ownLive is null
            ? L.Location_SharingStoppedFromAnotherDevice
            : L.Location_SharingMovedToAnotherDevice;
        await Dispatcher.InvokeAsync(() => ToastUI.Show(text, "icon-marker-pin", ToastDismissDelay.Short))
            .ConfigureAwait(false);
    }

    private async Task<ActiveShare[]> InitializeShares(ActiveShare[] activeShares, CancellationToken cancellationToken)
    {
        if (activeShares.All(x => x.LocationId is not null))
            return activeShares;

        if (await Tracker.Get(false, cancellationToken).ConfigureAwait(false) is not { Point: var point })
            return activeShares;

        return await activeShares.Select(x => InitializeShare(x, point, cancellationToken))
            .Collect(cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task<ActiveShare> InitializeShare(
        ActiveShare share,
        GeoPoint point,
        CancellationToken cancellationToken)
    {
        if (share.LocationId is not null)
            return share;

        var diff = new SharedLocationDiff { Point = point, LiveDuration = share.Duration };
        var change = Change.Create(diff);
        var sharedLocation = await Commander.Call(
                new SharedLocations_Change { Session = Session, ChatId = share.ChatId, Id = null, Change = change },
                cancellationToken)
            .ConfigureAwait(false);
        if (sharedLocation is null)
            return share;

        var command = new Chats_UpsertEntry {
            Session = Session,
            ChatId = share.ChatId,
            LocalId = null,
            LocationId = sharedLocation.Id,
        };
        await Commander.Call(command, cancellationToken).ConfigureAwait(false);
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

    private void DropExpiredShares()
    {
        // The server ends an expired share on its own - its LiveDuration is what ListLive filters by -
        // so unlike StopSharing this needs no StopServerShares call.
        lock (_lock)
            _shares.Value = WithoutExpired(_shares.Value);
    }

    private ActiveShare[] WithoutExpired(ActiveShare[] shares)
        => shares.Where(x => x.ExpiresAt > ServerNow).ToArray();

    // Nested types

    /// <summary>
    /// Compares share lists ignoring <see cref="ActiveShare.LocationId"/>,
    /// so a share id assigned mid-run doesn't restart <see cref="ReportLoop"/>.
    /// </summary>
    private sealed class ActiveSharesComparer : IEqualityComparer<ActiveShare[]>, IEqualityComparer<ActiveShare>
    {
        public static readonly ActiveSharesComparer Instance = new ();

        public bool Equals(ActiveShare[]? x, ActiveShare[]? y)
            => ReferenceEquals(x, y)
                || (x is not null && y is not null && x.SequenceEqual(y, this));

        public int GetHashCode(ActiveShare[] obj)
        {
            var hashCode = new HashCode();
            foreach (var share in obj)
                hashCode.Add(GetHashCode(share));
            return hashCode.ToHashCode();
        }

        public bool Equals(ActiveShare? x, ActiveShare? y)
            => ReferenceEquals(x, y)
                || (x is not null && y is not null
                    && x.ChatId == y.ChatId && x.StartedAt == y.StartedAt && x.Duration == y.Duration);

        public int GetHashCode(ActiveShare obj)
            => HashCode.Combine(obj.ChatId, obj.StartedAt, obj.Duration);
    }
}
