using ActualChat.UI.Blazor.Services;
using ActualLab.Rpc;

namespace ActualChat.UI.Blazor.App.Services;

public class AppPresenceReporter : UIWorkerBase<AppUIHub>, IComputeService
{
    private readonly MutableState<Moment> _lastCheckInAt;

    private UserActivityUI UserActivityUI => Hub.UserActivityUI;
    private ActiveChatsUI ActiveChatsUI => Hub.ActiveChatsUI;
    private RpcHub RpcHub => Hub.RpcHub;
    private Moment CpuNow => Clocks.CpuClock.Now;

    public AppPresenceReporter(AppUIHub hub) : base(hub)
        => _lastCheckInAt = StateFactory.NewMutable(
            CpuNow - Constants.Presence.OfflineTimeout,
            StateCategories.Get(GetType(), nameof(_lastCheckInAt)));

    protected override async Task OnRun(CancellationToken cancellationToken)
    {
        var awayTimeout = Constants.Presence.AwayTimeout;
        var activeCheckInPeriod = awayTimeout * 0.75;
        var inactiveCheckInPeriod = awayTimeout * 3;

        var cIsActive = await Computed
            .Capture(() => IsActive(cancellationToken), cancellationToken)
            .ConfigureAwait(false);
        var prevIsActive = false;
        while (!cancellationToken.IsCancellationRequested) {
            var isActive = cIsActive.Value;
            var isStateChange = isActive != prevIsActive;
            var checkInPeriod = isActive ? activeCheckInPeriod : inactiveCheckInPeriod;
            var dueIn = _lastCheckInAt.Value + checkInPeriod - CpuNow;
            if (isStateChange || dueIn <= TimeSpan.Zero) {
                await CheckIn(isActive, cancellationToken).ConfigureAwait(false);
                prevIsActive = isActive;
                continue;
            }

            using var delayCts = cancellationToken.CreateLinkedTokenSource();
            var whenInvalidated = cIsActive.WhenInvalidated(delayCts.Token);
            var whenDue = Task.Delay(dueIn, delayCts.Token);
            await Task.WhenAny(whenInvalidated, whenDue).ConfigureAwait(false);
            delayCts.CancelAndDisposeSilently();

            if (cIsActive.IsInvalidated())
                cIsActive = await cIsActive.Update(cancellationToken).ConfigureAwait(false);
        }
    }

    [ComputeMethod]
    protected virtual async Task<bool> IsActive(CancellationToken cancellationToken)
    {
        var now = CpuNow;
        var activeUntil = await GetActiveUntil(cancellationToken).ConfigureAwait(false);
        if (activeUntil <= now)
            return false;

        Computed.GetCurrent().Invalidate(activeUntil - TimeSpan.FromSeconds(0.25) - now);
        return true;
    }

    [ComputeMethod]
    protected virtual async Task<Moment> GetActiveUntil(CancellationToken cancellationToken)
    {
        var now = CpuNow;
        if (ActiveChatsUI.ActiveChats.Value.Any(c => c.IsRecording))
            return WithAutoInvalidation(CpuNow + Constants.Presence.ActivityPeriod);

        var activeUntil = await UserActivityUI.ActiveUntil.Use(cancellationToken).ConfigureAwait(false);
        return activeUntil > now
            ? WithAutoInvalidation(activeUntil)
            : activeUntil;

        Moment WithAutoInvalidation(Moment result) {
            Computed.GetCurrent().Invalidate(result - now);
            return result;
        }
    }

    // Private methods

    private async Task CheckIn(bool isActive, CancellationToken cancellationToken)
    {
        try {
            try {
                await RpcHub
                    .WhenClientPeerConnected(cancellationToken)
                    .WaitAsync(Constants.Presence.CheckInClientConnectTimeout, cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (TimeoutException) {
                // Do not log timeout errors, since it's expected. Just retry.
                _lastCheckInAt.Value += Constants.Presence.CheckInRetryDelay;
                return;
            }
            await Commander.Call(new UserPresences_CheckIn {
                Session = Session,
                IsActive = isActive,
            }, cancellationToken).ConfigureAwait(false);
            _lastCheckInAt.Value = CpuNow;
        }
        catch (Exception e) when (e is not OperationCanceledException) {
            Log.LogError(e, "CheckIn postponed, error: {ErrorType}({Message})", e.GetType().GetName(), e.Message);
            _lastCheckInAt.Value += Constants.Presence.CheckInRetryDelay;
        }
    }
}
