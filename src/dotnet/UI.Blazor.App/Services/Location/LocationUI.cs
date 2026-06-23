namespace ActualChat.UI.Blazor.App.Services;

/// <summary>
/// Drives the local user's live-location share: starts/stops the platform
/// <see cref="ILocationTracker"/> and pushes <see cref="LiveLocations_Report"/> every
/// <see cref="Constants.LiveLocation.UpdatePeriod"/> until the share is stopped or expires.
/// </summary>
public class LocationUI : UIWorkerBase<AppUIHub>, IComputeService
{
    private readonly MutableState<ActiveShare?> _share;

    private ILocationTracker Tracker => field ??= Hub.Services.GetRequiredService<ILocationTracker>();
    private Moment CpuNow => Clocks.CpuClock.Now;

    public LocationUI(AppUIHub hub) : base(hub)
        => _share = StateFactory.NewMutable(
            (ActiveShare?)null,
            StateCategories.Get(GetType(), nameof(_share)));

    public Task StartSharing(ChatId chatId, TimeSpan duration, CancellationToken cancellationToken)
    {
        var maxDuration = Constants.LiveLocation.MaxDuration;
        if (duration > maxDuration)
            duration = maxDuration;

        _share.Value = new ActiveShare(chatId, CpuNow + duration);
        return Task.CompletedTask;
    }

    public async Task StopSharing(ChatId chatId, CancellationToken cancellationToken)
    {
        if (_share.Value is { } share && share.ChatId == chatId)
            _share.Value = null;

        await Stop(chatId, cancellationToken).ConfigureAwait(false);
    }

    protected override async Task OnRun(CancellationToken cancellationToken)
    {
        var tracker = Tracker;
        var started = false;
        ChatId? trackingChatId = null;
        var lastPushAt = CpuNow;
        while (!cancellationToken.IsCancellationRequested) {
            var share = _share.Value;
            if (share is { } s && s.ExpiresAt <= CpuNow) {
                await Stop(s.ChatId, cancellationToken).ConfigureAwait(false);
                _share.Value = null;
                share = null;
            }
            if (share is null) {
                if (tracker.IsTracking)
                    await tracker.Stop(cancellationToken).ConfigureAwait(false);
                started = false;
                await _share.Computed.When(x => x is not null, cancellationToken).ConfigureAwait(false);
                continue;
            }

            if (started && trackingChatId is { } tc && tc != share.ChatId) {
                await Stop(tc, cancellationToken).ConfigureAwait(false);
                started = false;
            }
            trackingChatId = share.ChatId;
            if (!tracker.IsTracking)
                await tracker.Start(cancellationToken).ConfigureAwait(false);

            var point = tracker.LastKnown.Value;
            var isPushDue = !started || CpuNow - lastPushAt >= Constants.LiveLocation.UpdatePeriod;
            if (point is not null && isPushDue) {
                await Push(share, point, started, cancellationToken).ConfigureAwait(false);
                started = true;
                lastPushAt = CpuNow;
            }

            var dueIn = started
                ? lastPushAt + Constants.LiveLocation.UpdatePeriod - CpuNow
                : Constants.LiveLocation.UpdatePeriod;
            var untilExpiry = share.ExpiresAt - CpuNow;
            if (untilExpiry < dueIn)
                dueIn = untilExpiry;
            if (dueIn < TimeSpan.Zero)
                dueIn = TimeSpan.Zero;

            using var delayCts = cancellationToken.CreateLinkedTokenSource();
            var whenShareChanged = _share.Computed.WhenInvalidated(delayCts.Token);
            var whenLocationChanged = tracker.LastKnown.Computed.WhenInvalidated(delayCts.Token);
            var whenDue = Task.Delay(dueIn, delayCts.Token);
            await Task.WhenAny(whenShareChanged, whenLocationChanged, whenDue).ConfigureAwait(false);
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
}
