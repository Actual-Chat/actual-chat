namespace ActualChat.UI.Blazor.App.Services;

/// <summary>
/// Drives the local user's live-location shares: starts/stops the platform
/// <see cref="ILocationTracker"/> and pushes <see cref="LiveLocations_Report"/> every
/// <see cref="Constants.LiveLocation.UpdatePeriod"/> to every chat the user is sharing to,
/// until each share is stopped or expires.
/// </summary>
public class LocationUI : UIWorkerBase<AppUIHub>, IComputeService
{
    private readonly MutableState<ImmutableDictionary<ChatId, ActiveShare>> _shares;

    private ILocationTracker Tracker => field ??= Hub.Services.GetRequiredService<ILocationTracker>();
    private Moment CpuNow => Clocks.CpuClock.Now;

    public LocationUI(AppUIHub hub) : base(hub)
        => _shares = StateFactory.NewMutable(
            ImmutableDictionary<ChatId, ActiveShare>.Empty,
            StateCategories.Get(GetType(), nameof(_shares)));

    public Task StartSharing(ChatId chatId, TimeSpan duration, CancellationToken cancellationToken)
    {
        _shares.Value = _shares.Value.SetItem(chatId, new ActiveShare(chatId, CpuNow + duration));
        return Task.CompletedTask;
    }

    public async Task StopSharing(ChatId chatId, CancellationToken cancellationToken)
    {
        _shares.Value = _shares.Value.Remove(chatId);
        await Stop(chatId, cancellationToken).ConfigureAwait(false);
    }

    protected override async Task OnRun(CancellationToken cancellationToken)
    {
        var tracker = Tracker;
        var pushStates = new Dictionary<ChatId, PushState>();
        var period = Constants.LiveLocation.UpdatePeriod;
        while (!cancellationToken.IsCancellationRequested) {
            var now = CpuNow;
            var shares = _shares.Value;

            foreach (var share in shares.Values.Where(x => x.ExpiresAt <= now).ToList()) {
                await Stop(share.ChatId, cancellationToken).ConfigureAwait(false);
                shares = shares.Remove(share.ChatId);
            }
            if (!ReferenceEquals(shares, _shares.Value))
                _shares.Value = shares;

            foreach (var chatId in pushStates.Keys.Where(x => !shares.ContainsKey(x)).ToList())
                pushStates.Remove(chatId);

            if (shares.IsEmpty) {
                if (tracker.IsTracking)
                    await tracker.Stop(cancellationToken).ConfigureAwait(false);
                await _shares.Computed.When(x => !x.IsEmpty, cancellationToken).ConfigureAwait(false);
                continue;
            }

            if (!tracker.IsTracking)
                await tracker.Start(cancellationToken).ConfigureAwait(false);

            var point = tracker.LastKnown.Value;
            var dueIn = period;
            foreach (var share in shares.Values) {
                pushStates.TryGetValue(share.ChatId, out var state);
                var isPushDue = !state.Started || now - state.LastPushAt >= period;
                if (point is not null && isPushDue) {
                    await Push(share, point, state.Started, cancellationToken).ConfigureAwait(false);
                    state = new PushState(true, now);
                    pushStates[share.ChatId] = state;
                }

                var shareDueIn = state.Started ? state.LastPushAt + period - now : period;
                var untilExpiry = share.ExpiresAt - now;
                if (untilExpiry < shareDueIn)
                    shareDueIn = untilExpiry;
                if (shareDueIn < dueIn)
                    dueIn = shareDueIn;
            }
            if (dueIn < TimeSpan.Zero)
                dueIn = TimeSpan.Zero;

            using var delayCts = cancellationToken.CreateLinkedTokenSource();
            var whenSharesChanged = _shares.Computed.WhenInvalidated(delayCts.Token);
            var whenLocationChanged = tracker.LastKnown.Computed.WhenInvalidated(delayCts.Token);
            var whenDue = Task.Delay(dueIn, delayCts.Token);
            await Task.WhenAny(whenSharesChanged, whenLocationChanged, whenDue).ConfigureAwait(false);
            delayCts.CancelAndDisposeSilently();
        }
    }

    // Private methods

    private async Task Push(ActiveShare share, GeoPoint point, bool alreadyStarted, CancellationToken cancellationToken)
    {
        try {
            // The first push carries the remaining duration to start the share; later pushes
            // omit it (null) and only update the position.
            TimeSpan? duration = null;
            if (!alreadyStarted) {
                var remaining = share.ExpiresAt - CpuNow;
                duration = remaining < TimeSpan.Zero ? TimeSpan.Zero : remaining;
            }
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

    // Nested types

    private sealed record ActiveShare(ChatId ChatId, Moment ExpiresAt);

    private readonly record struct PushState(bool Started, Moment LastPushAt);
}
