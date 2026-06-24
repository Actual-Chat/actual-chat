using ActualChat.Kvas;

namespace ActualChat.UI.Blazor.App.Services;

/// <summary>
/// Drives the local user's live-location shares: while at least one chat is being shared it runs a
/// <see cref="FuncWorker"/> that starts the platform <see cref="ILocationTracker"/> and pushes
/// <see cref="LiveLocations_Report"/> to every shared chat once per <see cref="Constants.LiveLocation.UpdatePeriod"/>.
/// The worker exists only while there are shares; shares are persisted locally and resumed on restart.
/// </summary>
public class LocationUI : UIWorkerBase<AppUIHub>, IComputeService
{
    private readonly StoredState<ActiveShare[]> _shares;
    private FuncWorker? _worker;

    private ILocationTracker Tracker => field ??= Hub.Services.GetRequiredService<ILocationTracker>();
    private Moment CpuNow => Clocks.CpuClock.Now;
    private Moment ServerNow => Clocks.ServerClock.Now;

    public LocationUI(AppUIHub hub) : base(hub)
        => _shares = StateFactory.NewKvasStored<ActiveShare[]>(
            new (LocalSettings, nameof(ActiveShare)) {
                InitialValue = [],
                Corrector = DropExpired,
                Category = StateCategories.Get(GetType(), nameof(_shares)),
            });

    public Task StartSharing(ChatId chatId, TimeSpan duration, CancellationToken cancellationToken)
    {
        var share = new ActiveShare(chatId, ServerNow + duration);
        lock (Lock) {
            _shares.Value = _shares.Value.Where(x => x.ChatId != chatId).Append(share).ToArray();
            EnsureWorker();
        }
        return Task.CompletedTask;
    }

    public async Task StopSharing(ChatId chatId, CancellationToken cancellationToken)
    {
        lock (Lock)
            _shares.Value = _shares.Value.Where(x => x.ChatId != chatId).ToArray();
        await Stop(chatId, cancellationToken).ConfigureAwait(false);
    }

    protected override async Task OnRun(CancellationToken cancellationToken)
    {
        // Bootstrap only: resume persisted shares once the store is read, then return - no parked loop.
        await _shares.WhenRead.WaitAsync(cancellationToken).ConfigureAwait(false);
        lock (Lock)
            EnsureWorker();
    }

    // Private methods

    private void EnsureWorker()
    {
        // Lock must be held. Spins the push worker up on demand; PushLoop tears it down when no shares remain.
        if (_worker == null && _shares.Value.Length != 0 && !StopToken.IsCancellationRequested)
            _worker = FuncWorker.Start(PushLoop, StopToken);
    }

    private async Task PushLoop(CancellationToken cancellationToken)
    {
        var started = new HashSet<ChatId>();
        var period = Constants.LiveLocation.UpdatePeriod;
        var lastPushAt = CpuNow;
        await Tracker.Start(cancellationToken).ConfigureAwait(false);
        try {
            while (!cancellationToken.IsCancellationRequested) {
                var shares = await StopExpired().ConfigureAwait(false);
                if (shares.Length == 0)
                    return;

                started.RemoveWhere(id => shares.All(x => x.ChatId != id));
                var point = Tracker.LastKnown.Value;
                var hasUnstarted = shares.Any(x => !started.Contains(x.ChatId));
                if (point is not null && (hasUnstarted || CpuNow - lastPushAt >= period)) {
                    // The first push to a chat carries its remaining duration to start the share on the
                    // server; later pushes omit it (null) so the server only updates the position and
                    // doesn't reset/re-clamp the share window. After a restart every chat is "first" again,
                    // so the stored expiry is re-established from its remaining window.
                    foreach (var share in shares) {
                        var duration = started.Add(share.ChatId) ? Remaining(share) : (TimeSpan?)null;
                        await Push(share, point, duration, cancellationToken).ConfigureAwait(false);
                    }
                    lastPushAt = CpuNow;
                }

                var dueIn = point is null ? period : lastPushAt + period - CpuNow;
                foreach (var share in shares) {
                    var untilExpiry = share.ExpiresAt - ServerNow;
                    if (untilExpiry < dueIn)
                        dueIn = untilExpiry;
                }
                if (dueIn < TimeSpan.Zero)
                    dueIn = TimeSpan.Zero;

                using var delayCts = cancellationToken.CreateLinkedTokenSource();
                var whenSharesChanged = _shares.Computed.WhenInvalidated(delayCts.Token);
                var whenLocationChanged = Tracker.LastKnown.Computed.WhenInvalidated(delayCts.Token);
                var whenDue = Task.Delay(dueIn, delayCts.Token);
                await Task.WhenAny(whenSharesChanged, whenLocationChanged, whenDue).ConfigureAwait(false);
                delayCts.CancelAndDisposeSilently();
            }
        }
        finally {
            // Stop the tracker before releasing the worker slot, so a worker restarted for a share added
            // during teardown re-acquires the tracker only after this stop completes (no start/stop race).
            await Tracker.Stop(CancellationToken.None).ConfigureAwait(false);
            lock (Lock) {
                _worker = null;
                EnsureWorker();
            }
        }
    }

    private async Task<ActiveShare[]> StopExpired()
    {
        ActiveShare[] live, expired;
        lock (Lock) {
            var shares = _shares.Value;
            live = shares.Where(x => x.ExpiresAt > ServerNow).ToArray();
            if (live.Length == shares.Length)
                return shares;

            expired = shares.Where(x => x.ExpiresAt <= ServerNow).ToArray();
            _shares.Value = live;
        }
        foreach (var share in expired)
            await Stop(share.ChatId, CancellationToken.None).ConfigureAwait(false);
        return live;
    }

    private ValueTask<ActiveShare[]> DropExpired(ActiveShare[] shares, CancellationToken cancellationToken)
        => new (shares.Where(x => x.ExpiresAt > ServerNow).ToArray());

    private TimeSpan Remaining(ActiveShare share)
    {
        var remaining = share.ExpiresAt - ServerNow;
        return remaining < TimeSpan.Zero ? TimeSpan.Zero : remaining;
    }

    private async Task Push(ActiveShare share, GeoPoint point, TimeSpan? duration, CancellationToken cancellationToken)
    {
        try {
            await Commander.Call(new LiveLocations_Report(Session, share.ChatId, point, duration), cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception e) when (e is not OperationCanceledException) {
            Log.LogError(e, "Push failed for chat {ChatId}", share.ChatId);
        }
    }

    private async Task Stop(ChatId chatId, CancellationToken cancellationToken)
    {
        if (chatId.Value.IsNullOrEmpty())
            return;

        try {
            await Commander.Call(new LiveLocations_Stop(Session, chatId), cancellationToken).ConfigureAwait(false);
        }
        catch (Exception e) when (e is not OperationCanceledException) {
            Log.LogError(e, "Stop failed for chat {ChatId}", chatId);
        }
    }
}
