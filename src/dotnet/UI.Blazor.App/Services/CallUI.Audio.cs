using ActualChat.Live;
using ActualChat.UI.Blazor.Services;

namespace ActualChat.UI.Blazor.App.Services;

public partial class CallUI
{
    // The output picked for the call on the line; null is the platform's default. The pick is this
    // call's only, and a device that arrives mid-call takes over: plugging something in says where
    // you want to listen.
    private readonly MutableState<string?> _pickedOutputRouteId;

    private AudioFocusUI AudioFocusUI => Hub.AudioFocusUI;
    private ChatVideoUI ChatVideoUI => Hub.ChatVideoUI;

    // Public methods

    [ComputeMethod]
    public virtual async Task<AudioOutputRoutes> GetOutputRoutes(CancellationToken cancellationToken)
    {
        if (AudioFocusUI.OutputRoutes is not { } outputRoutes)
            return AudioOutputRoutes.None;

        var routes = await outputRoutes.Use(cancellationToken).ConfigureAwait(false);
        var pickedId = await _pickedOutputRouteId.Use(cancellationToken).ConfigureAwait(false);
        // Shown as picked from the tap itself; the platform's view catches up once the switch lands.
        return pickedId is not null && routes.Routes.Any(x => x.Id == pickedId)
            ? routes with { CurrentId = pickedId }
            : routes;
    }

    public void SelectOutputRoute(string routeId)
        => _pickedOutputRouteId.Value = routeId;

    // Protected/internal methods

    [ComputeMethod]
    protected virtual async Task<CallActivity> GetCallActivity(CancellationToken cancellationToken)
    {
        var call = await GetActiveCall(cancellationToken).ConfigureAwait(false);
        if (call is not { Phase: CallPhase.Active or CallPhase.Dialing })
            return CallActivity.None;

        // HasVideo is how the call was placed; a camera turned on mid-call holds the phone out all the same.
        var hasVideo = call.HasVideo
            || await ChatVideoUI.IsOwnCameraRecording(call.ChatId, cancellationToken).ConfigureAwait(false)
            || await ChatVideoUI.IsAnyoneVideoStreaming(call.ChatId, cancellationToken).ConfigureAwait(false);
        return new CallActivity(true, hasVideo);
    }

    [ComputeMethod]
    protected virtual async Task<string?> GetOutputRouteToApply(CancellationToken cancellationToken)
    {
        var call = await GetActiveCall(cancellationToken).ConfigureAwait(false);
        if (call is not { Phase: CallPhase.Active or CallPhase.Dialing })
            return null;

        return await _pickedOutputRouteId.Use(cancellationToken).ConfigureAwait(false);
    }

    [ComputeMethod]
    protected virtual async Task<bool> MustTurnScreenOffAtEar(CancellationToken cancellationToken)
    {
        var activity = await GetCallActivity(cancellationToken).ConfigureAwait(false);
        if (!activity.IsCallActive)
            return false;

        var routes = await GetOutputRoutes(cancellationToken).ConfigureAwait(false);
        return routes.Current?.Kind == AudioOutputKind.Phone;
    }

    // Private methods

    private async Task SyncCallActivity(CancellationToken cancellationToken)
    {
        // Read from the slot, whoever wrote it: an accept and a placed call write it themselves, ahead
        // of the server's answer, and the platform must learn of them all the same. The session's
        // category follows this, and a call on the line must keep the one that can reach the
        // earpiece even while the mic is off.
        var cActivity = await Computed
            .Capture(() => GetCallActivity(cancellationToken), cancellationToken)
            .ConfigureAwait(false);
        var activity = CallActivity.None;
        await foreach (var c in cActivity.Changes(cancellationToken).ConfigureAwait(false)) {
            // Video sticks for the rest of the call: a camera toggled off and on again mustn't bounce
            // the sound between the ear and the speaker.
            var nextActivity = c.Value.IsCallActive && activity.HasVideo
                ? c.Value with { HasVideo = true }
                : c.Value;
            if (nextActivity == activity)
                continue;

            var isVideoStarted = activity.IsCallActive && !activity.HasVideo && nextActivity.HasVideo;
            activity = nextActivity;
            // The pick belongs to the call that just ended; the next one starts from the defaults.
            if (!activity.IsCallActive)
                _pickedOutputRouteId.Value = null;
            else if (isVideoStarted && _pickedOutputRouteId.Value == AudioOutputRoute.PhoneId) {
                Log.LogInformation("Video started mid-call, dropping the earpiece pick");
                _pickedOutputRouteId.Value = null;
            }
            await AudioFocusUI.SetCallActive(activity.IsCallActive, activity.HasVideo).ConfigureAwait(false);
        }
    }

    private async Task SyncOutputRoute(CancellationToken cancellationToken)
    {
        var cRouteId = await Computed
            .Capture(() => GetOutputRouteToApply(cancellationToken), cancellationToken)
            .ConfigureAwait(false);
        string? routeId = null;
        await foreach (var c in cRouteId.Changes(cancellationToken).ConfigureAwait(false)) {
            if (c.Value == routeId)
                continue;

            routeId = c.Value;
            await AudioFocusUI.ApplyOutputRoute(routeId).ConfigureAwait(false);
        }
    }

    private async Task SyncScreenOffAtEar(CancellationToken cancellationToken)
    {
        var cMustTurnOff = await Computed
            .Capture(() => MustTurnScreenOffAtEar(cancellationToken), cancellationToken)
            .ConfigureAwait(false);
        var mustTurnOff = false;
        try {
            await foreach (var c in cMustTurnOff.Changes(cancellationToken).ConfigureAwait(false)) {
                if (c.Value == mustTurnOff)
                    continue;

                mustTurnOff = c.Value;
                await Hub.KeepAwakeUI.SetScreenOffAtEar(mustTurnOff).ConfigureAwait(false);
            }
        }
        finally {
            // A screen left blankable by a dead scope would go dark at every cover until the app restarts.
            if (mustTurnOff)
                await Hub.KeepAwakeUI.SetScreenOffAtEar(false).SilentAwait(false);
        }
    }

    private async Task SyncOutputRouteTakeover(CancellationToken cancellationToken)
    {
        if (AudioFocusUI.OutputRoutes is not { } outputRoutes)
            return;

        var cRoutes = await Computed
            .Capture(() => outputRoutes.Use(cancellationToken), cancellationToken)
            .ConfigureAwait(false);
        var externalIds = new HashSet<string>();
        await foreach (var c in cRoutes.Changes(cancellationToken).ConfigureAwait(false)) {
            var arrived = c.Value.Routes.Where(x => x.IsExternal && externalIds.Add(x.Id)).ToList();
            externalIds.IntersectWith(c.Value.Routes.Select(x => x.Id));
            if (arrived.Count == 0 || _pickedOutputRouteId.Value is null)
                continue;

            Log.LogInformation("Output {Ids} arrived mid-call, dropping the pick", arrived.Select(x => x.Id));
            _pickedOutputRouteId.Value = null;
        }
    }

    // Nested types

    public sealed record CallActivity(bool IsCallActive, bool HasVideo)
    {
        public static readonly CallActivity None = new(false, false);
    }
}
