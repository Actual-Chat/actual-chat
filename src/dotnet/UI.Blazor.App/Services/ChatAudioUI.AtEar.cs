using ActualChat.Live;
using ActualChat.UI.Blazor.App.Services.Gestures;
using ActualChat.UI.Blazor.Services;

namespace ActualChat.UI.Blazor.App.Services;

// A replay on the phone's own speaker, held to the ear, is heard like a phone call: it moves to the
// earpiece, the screen goes off (CallUI owns that switch), and both come back once the phone is
// taken away. On a headset or in a car it's plain media playback, and the sensor stays off.
public partial class ChatAudioUI
{
    // Android phones with an ultrasound proximity sensor measure through the active speaker, so
    // the sensor restarts on every output switch and can report "far" at the ear until it settles.
    private static readonly TimeSpan EarSensorSettleTime =
        OperatingSystem.IsAndroid() ? TimeSpan.FromSeconds(1) : TimeSpan.Zero;

    private readonly MutableState<bool> _isProximityCovered;

    private SensorFeed SensorFeed => field ??= Hub.Services.GetRequiredService<SensorFeed>();

    [ComputeMethod]
    public virtual async Task<bool> MustSenseReplayAtEar(CancellationToken cancellationToken)
    {
        if (!SensorFeed.IsProximityAvailable || AudioFocusUI.OutputKind is not { } outputKindState)
            return false;

        var replayState = await ReplayState.Use(cancellationToken).ConfigureAwait(false);
        if (replayState is not { PausedAt: null })
            return false;

        // A call on the line has an output of its own to keep.
        var call = await Hub.CallUI.GetActiveCall(cancellationToken).ConfigureAwait(false);
        if (call is { Phase: CallPhase.Active or CallPhase.Dialing })
            return false;

        // Android Auto carries the replay without showing up as an output device.
        var carAudioRoute = await GetCarAudioRoute(cancellationToken).ConfigureAwait(false);
        if (carAudioRoute != CarAudioRoute.Default)
            return false;

        // The earpiece counts: that's where the replay is while it's held to the ear.
        var outputKind = await outputKindState.Use(cancellationToken).ConfigureAwait(false);
        return outputKind is AudioOutputKind.Speaker or AudioOutputKind.Phone;
    }

    // Protected/internal methods

    [ComputeMethod]
    protected virtual async Task<bool> IsReplayAtEar(CancellationToken cancellationToken)
    {
        var mustSense = await MustSenseReplayAtEar(cancellationToken).ConfigureAwait(false);
        return mustSense && await _isProximityCovered.Use(cancellationToken).ConfigureAwait(false);
    }

    // Private methods

    private async Task SyncReplayProximitySensing(CancellationToken cancellationToken)
    {
        var cMustSense = await Computed
            .Capture(() => MustSenseReplayAtEar(cancellationToken), cancellationToken)
            .ConfigureAwait(false);
        var mustSense = false;
        try {
            await foreach (var c in cMustSense.Changes(cancellationToken).ConfigureAwait(false)) {
                if (c.Value == mustSense)
                    continue;

                mustSense = c.Value;
                if (mustSense)
                    StartProximitySensing();
                else
                    StopProximitySensing();
            }
        }
        finally {
            if (mustSense)
                StopProximitySensing();
        }
    }

    private void StartProximitySensing()
    {
        SensorFeed.ProximityChanged += OnProximityChanged;
        SensorFeed.StartProximity(this);
    }

    private void StopProximitySensing()
    {
        SensorFeed.StopProximity(this);
        SensorFeed.ProximityChanged -= OnProximityChanged;
        _isProximityCovered.Value = false;
    }

    private void OnProximityChanged(bool isCovered)
        => _isProximityCovered.Value = isCovered;

    private async Task SyncPlaybackAtEar(CancellationToken cancellationToken)
    {
        var cIsAtEar = await Computed
            .Capture(() => IsReplayAtEar(cancellationToken), cancellationToken)
            .ConfigureAwait(false);
        var isAtEar = false;
        var atEarSince = CpuNow;
        try {
            await foreach (var c in cIsAtEar.Changes(cancellationToken).ConfigureAwait(false)) {
                if (c.Value == isAtEar)
                    continue;

                if (!c.Value && await IsEarReleaseRetracted(atEarSince, cancellationToken).ConfigureAwait(false))
                    continue;

                isAtEar = c.Value;
                await AudioFocusUI.SetPlaybackAtEar(isAtEar).ConfigureAwait(false);
                atEarSince = CpuNow;
            }
        }
        finally {
            // Left on, the next playback from this device would start on the earpiece.
            if (isAtEar)
                await AudioFocusUI.SetPlaybackAtEar(false).SilentAwait(false);
        }
    }

    private async Task<bool> IsEarReleaseRetracted(Moment atEarSince, CancellationToken cancellationToken)
    {
        // Only the sensor's word is doubted, and only while it settles after the switch to the
        // earpiece: a replay that ended leaves the earpiece at once, and so does a phone lowered later.
        var settleDelay = atEarSince + EarSensorSettleTime - CpuNow;
        if (settleDelay <= TimeSpan.Zero
            || !await MustSenseReplayAtEar(cancellationToken).ConfigureAwait(false))
            return false;

        await Task.Delay(settleDelay, cancellationToken).ConfigureAwait(false);
        return await IsReplayAtEar(cancellationToken).ConfigureAwait(false);
    }
}
