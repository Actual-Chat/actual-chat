using ActualChat.Chat.UI.Blazor.Services;
using ActualChat.UI.Blazor.Services;
using ActualChat.Users;
using ActualLab.Rpc;

namespace ActualChat.UI.Blazor.App.Services;

public class AppPresenceReporter : ScopedWorkerBase<ChatUIHub>, IComputeService
{
    private IMutableState<Moment> _lastCheckInAt;

    private UserActivityUI UserActivityUI => Hub.UserActivityUI;
    private ChatAudioUI ChatAudioUI => Hub.ChatAudioUI;
    private RpcHub RpcHub => Hub.RpcHub();
    private Moment CpuNow => Clocks.CpuClock.Now;

    public AppPresenceReporter(ChatUIHub hub) : base(hub)
        => _lastCheckInAt = StateFactory.NewMutable(
            CpuNow - Constants.Presence.OfflineTimeout,
            StateCategories.Get(GetType(), nameof(_lastCheckInAt)));

    protected override async Task OnRun(CancellationToken cancellationToken)
    {
        var cIsActive = await Computed
            .Capture(() => IsActive(cancellationToken), cancellationToken)
            .ConfigureAwait(false);
        var prevIsActive = false;
        await foreach (var change in cIsActive.Changes(cancellationToken).ConfigureAwait(false)) {
            var isActive = change.Value;
            // throttle since IsActive depends on LastCheckInAt
            if (isActive == prevIsActive && CpuNow - _lastCheckInAt.Value < Constants.Presence.CheckInPeriod * 0.7)
                continue;

            await CheckIn(isActive, cancellationToken).ConfigureAwait(false);
            prevIsActive = isActive;
        }
    }

    [ComputeMethod]
    protected virtual async Task<bool> IsActive(CancellationToken cancellationToken)
    {
        var now = CpuNow;
        var lastCheckInAt = await _lastCheckInAt.Use(cancellationToken).ConfigureAwait(false);
        var activeUntil = await GetActiveUntil(cancellationToken).ConfigureAwait(false);

        return activeUntil > now
            ? WithAutoInvalidation(activeUntil, true)
            : WithAutoInvalidation(lastCheckInAt + Constants.Presence.CheckInPeriod, false);

        bool WithAutoInvalidation(Moment invalidateAt, bool result) {
            Computed.GetCurrent()!.Invalidate(invalidateAt - now);
            return result;
        }
    }

    [ComputeMethod]
    protected virtual async Task<Moment> GetActiveUntil(CancellationToken cancellationToken)
    {
        var now = CpuNow;
        if (await ChatAudioUI.IsAudioOn().ConfigureAwait(false))
            return WithAutoInvalidation(CpuNow + Constants.Presence.ActivityPeriod);

        var activeUntil = await UserActivityUI.ActiveUntil.Use(cancellationToken).ConfigureAwait(false);
        var audioStoppedAt = await ChatAudioUI.AudioStoppedAt.Use(cancellationToken).ConfigureAwait(false);
        if (audioStoppedAt != null)
            activeUntil = Moment.Max(audioStoppedAt.Value + Constants.Presence.ActivityPeriod, activeUntil);

        if (activeUntil > now)
            return WithAutoInvalidation(activeUntil);

        return activeUntil;

        Moment WithAutoInvalidation(Moment result) {
            Computed.GetCurrent()!.Invalidate(result - now);
            return result;
        }
    }

    // Private methods

    private async Task CheckIn(bool isActive, CancellationToken cancellationToken)
    {
        try {
            await RpcHub
                .WhenClientPeerConnected(cancellationToken)
                .WaitAsync(Constants.Presence.CheckInClientConnectTimeout, cancellationToken)
                .ConfigureAwait(false);
            await Commander.Call(new UserPresences_CheckIn(Session, isActive), cancellationToken).ConfigureAwait(false);
            _lastCheckInAt.Value = CpuNow;
        }
        catch (Exception e) when (e is not OperationCanceledException) {
            var failureKind = e switch {
                DisconnectedException => "disconnected",
                TimeoutException => "timed out",
                _ => "failed",
            };
            Log.LogError(e, "CheckIn postponed ({FailureKind})", failureKind);
            _lastCheckInAt.Value += Constants.Presence.CheckInRetryDelay;
        }
    }
}
