using ActualChat.Bandwidth;
using ActualChat.Streaming;

namespace ActualChat.UI.Blazor.App.Services;

public sealed partial class VideoQualityUI
{
    private readonly Dictionary<VideoSourceKind, VideoRecorder> _recordersByKind = new();
    private readonly Dictionary<VideoSourceKind, RecorderStats> _lastRecorderStatsByKind = new();
    private readonly Dictionary<VideoSourceKind, RecordingQualityState> _lastRecordingStateByKind = new();
    private readonly Dictionary<VideoSourceKind, int> _lastRecordingSignalByKind = new();
    private readonly Dictionary<VideoSourceKind, RecordingQualityReason> _lastRecordingReasonByKind = new();
    private readonly Dictionary<VideoSourceKind, (long BytesAt, CpuTimestamp At)> _lastEncodedSampleByKind = new();
    private readonly Dictionary<VideoSourceKind, int> _lastAppliedTargetByKind = new();
    // Worker-restart cooldown: a worker.stop()/start() cycle resets the
    // RecorderStats counters to 0, producing a transient bytes/sec=0
    // tick that would otherwise feed BWE a fake bad signal and trigger
    // a cascading demote → another restart → another cooldown. Detect
    // the counter going backwards and skip RunOutboundTick for N ticks.
    private const int RestartCooldownTicks = 2;
    private readonly Dictionary<VideoSourceKind, int> _restartCooldownByKind = new();
    private readonly LayerCap _outboundLayers;
    private readonly EncodingCap _outboundEncodingCap;
    private readonly BandwidthCap _outboundBandwidthCap;
    private readonly BandwidthEstimator _outboundBwEstimator;
    private CpuTimestamp _outboundStartedAt;
    private CpuTimestamp _outboundLastEvalAt;

    public BandwidthEstimator OutboundBandwidthEstimator => _outboundBwEstimator;
    public LayerCap OutboundEncodingLayers => _outboundEncodingCap.Layers;
    public LayerCap OutboundBandwidthLayers => _outboundBandwidthCap.Layers;
    public int OutboundDeviceCameraCap => _outboundLayers.DeviceCameraCap;
    public int OutboundDeviceScreencastCap => _outboundLayers.ScreencastCap;

    /// <summary>
    /// Receives a <see cref="RecorderStats"/> from the worker
    /// (JS-side aggregator in <c>video-processing.ts</c>). Triggers a
    /// classification + aggregation step for the matching <see cref="VideoSourceKind"/>.
    /// </summary>
    public async Task OnRecorderStats(
        VideoSourceKind kind,
        RecorderStats snapshot,
        VideoRecorder recorder,
        CancellationToken cancellationToken)
    {
        _whenActuallyUsed.TrySetResult();
        _recordersByKind[kind] = recorder;

        // Worker restart detection: RecorderStats counters reset to 0
        // on worker.start(). If the new snapshot's monotonic counters
        // are below the prior snapshot's, the worker was restarted —
        // re-baseline the bytes sample, arm a cooldown so BWE doesn't
        // see the transient bytes=0 tick as bad signal, and reset
        // streak state across BWE + caps so a half-built bad streak
        // from before the restart doesn't immediately fire on the
        // first eval after the cooldown.
        var previous = _lastRecorderStatsByKind.GetValueOrDefault(kind);
        if (previous is not null
            && (snapshot.BytesEncoded < previous.BytesEncoded
                || snapshot.BundlesShipped < previous.BundlesShipped)) {
            _restartCooldownByKind[kind] = RestartCooldownTicks;
            _lastEncodedSampleByKind[kind] = (snapshot.BytesEncoded, CpuTimestamp.Now);
            _outboundBwEstimator.ResetStreaks();
            _outboundEncodingCap.ResetStreaks();
            _outboundBandwidthCap.ResetStreaks();
            Log.LogInformation(
                "OnRecorderStats: {Kind} worker restart detected (BytesEncoded {PrevB}->{NewB}, " +
                "BundlesShipped {PrevBundles}->{NewBundles}); skipping outbound tick for {Cooldown} ticks, " +
                "reset BWE+cap streaks",
                kind, previous.BytesEncoded, snapshot.BytesEncoded,
                previous.BundlesShipped, snapshot.BundlesShipped, RestartCooldownTicks);
        }

        _lastRecorderStatsByKind[kind] = snapshot;
        if (_coldStartTicksRemaining > 0) {
            _coldStartTicksRemaining--;
            _lastRecordingSignalByKind[kind] = 0;
            _lastRecordingReasonByKind[kind] = RecordingQualityReason.ColdStartTick;
            return;
        }

        // BWE / cap evaluation is global across kinds — if any kind is
        // still in restart cooldown, skip this tick entirely.
        if (IsAnyKindInRestartCooldown()) {
            if (_restartCooldownByKind.TryGetValue(kind, out var remaining) && remaining > 0)
                _restartCooldownByKind[kind] = remaining - 1;
            _lastRecordingReasonByKind[kind] = RecordingQualityReason.ColdStartTick;
            return;
        }

        if (_outboundStartedAt == default)
            _outboundStartedAt = CpuTimestamp.Now;
        if (!IsEvaluationDue(_outboundStartedAt, _outboundLastEvalAt)) {
            _lastRecordingReasonByKind[kind] = RecordingQualityReason.Stable;
            return;
        }
        _outboundLastEvalAt = CpuTimestamp.Now;

        await RunOutboundTick(cancellationToken).ConfigureAwait(false);
    }

    private bool IsAnyKindInRestartCooldown()
    {
        foreach (var (_, ticks) in _restartCooldownByKind) {
            if (ticks > 0)
                return true;
        }
        return false;
    }

    public RecordingQualitySnapshot GetRecordingSnapshot(VideoSourceKind kind)
    {
        var state = _lastRecordingStateByKind.GetValueOrDefault(kind);
        return new(
            kind,
            state,
            _lastRecorderStatsByKind.GetValueOrDefault(kind),
            _lastRecordingSignalByKind.GetValueOrDefault(kind),
            _lastRecordingReasonByKind.GetValueOrDefault(kind),
            _debugMaxRecordingLayerCount);
    }

    // Private methods

    private async Task RunOutboundTick(CancellationToken cancellationToken)
    {
        var fusedDropRatio = 0.0;
        var fusedAckAgeMs = -1.0;
        var fusedEncodeRatio = 0.0;
        long totalBytesPerSec = 0;
        var sampleAt = CpuTimestamp.Now;
        foreach (var (k, stats) in _lastRecorderStatsByKind) {
            fusedDropRatio = Math.Max(fusedDropRatio, stats.SenderFrameDropRatioEma);
            if (stats.LastAckAgeMs >= 0)
                fusedAckAgeMs = Math.Max(fusedAckAgeMs, stats.LastAckAgeMs);
            fusedEncodeRatio = Math.Max(fusedEncodeRatio, stats.EncodeRatioEma);
            totalBytesPerSec += ComputeAndUpdateBytesPerSec(k, stats.BytesEncoded, sampleAt);
        }

        var signalLevel = ComputeSenderSignalLevel(fusedDropRatio, fusedAckAgeMs, fusedEncodeRatio);
        var connection = ConnectivityUI.ConnectionInfo.Value;
        _outboundBwEstimator.Tick(connection, SystemClock.Now, totalBytesPerSec, signalLevel);
        var preEncCam = _outboundEncodingCap.Layers.CameraLayers;
        var preBwCam = _outboundBandwidthCap.Layers.CameraLayers;
        _outboundEncodingCap.Tick(fusedEncodeRatio);
        _outboundBandwidthCap.Tick(_outboundBwEstimator);
        var postEncCam = _outboundEncodingCap.Layers.CameraLayers;
        var postBwCam = _outboundBandwidthCap.Layers.CameraLayers;
        if (preEncCam != postEncCam || preBwCam != postBwCam)
            Log.LogInformation(
                "RunOutboundTick: cap changed — encCam {PreEnc}->{PostEnc} (encRatio={EncRatio:F2}, badStreak={BadStreak}, goodStreak={GoodStreak}), " +
                "bwCam {PreBw}->{PostBw} (signal={Signal:F2}, verdict={Verdict}, ceiling={CeilingBps}bps, current={CurrentBps}bps, " +
                "negStreak={NegStreak}, posStreak={PosStreak}), totalBps={TotalBps}, dropRatio={DropRatio:F3}, ackAgeMs={AckAgeMs:F0}",
                preEncCam, postEncCam, fusedEncodeRatio, _outboundEncodingCap.BadStreak, _outboundEncodingCap.GoodStreak,
                preBwCam, postBwCam, signalLevel, _outboundBwEstimator.LastVerdict,
                _outboundBwEstimator.CeilingBps, _outboundBwEstimator.LastCurrentBps,
                _outboundBwEstimator.NegativeStreak, _outboundBwEstimator.PositiveStreak,
                totalBytesPerSec * 8, fusedDropRatio, fusedAckAgeMs);

        var encLayers = _outboundEncodingCap.Layers;
        var bwLayers = _outboundBandwidthCap.Layers;
        var effCamera = Math.Min(encLayers.CameraLayers, bwLayers.CameraLayers);
        var effScreencast = Math.Min(encLayers.ScreencastLayers, bwLayers.ScreencastLayers);
        if (_debugMaxRecordingLayerCount is { } debugCap) {
            effCamera = Math.Min(effCamera, debugCap);
            effScreencast = Math.Min(effScreencast, debugCap);
        }

        foreach (var (k, recorder) in _recordersByKind) {
            var target = k == VideoSourceKind.Camera ? effCamera : effScreencast;
            target = Math.Max(1, target);
            await ApplyOutboundTarget(k, recorder, target, cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task ApplyOutboundTarget(
        VideoSourceKind kind,
        VideoRecorder recorder,
        int targetLayerCount,
        CancellationToken cancellationToken)
    {
        var previous = _lastAppliedTargetByKind.GetValueOrDefault(kind);
        var changed = previous != targetLayerCount;
        if (changed)
            await recorder.SetTargetLayerCount(targetLayerCount, cancellationToken).ConfigureAwait(false);
        _lastAppliedTargetByKind[kind] = targetLayerCount;

        var stats = _lastRecorderStatsByKind.GetValueOrDefault(kind);
        var state = new RecordingQualityState(targetLayerCount, targetLayerCount);
        _lastRecordingStateByKind[kind] = state;
        var reason = changed ? RecordingQualityReason.Backoff : RecordingQualityReason.Stable;
        _lastRecordingReasonByKind[kind] = reason;
        _lastRecordingSignalByKind[kind] = _outboundBwEstimator.LastVerdict switch {
            BandwidthVerdict.Bad => -1,
            BandwidthVerdict.Good => 1,
            _ => 0,
        };

        if (stats is not null) {
            var info = new RecordingQualityInfo(
                reason,
                stats.SenderFrameDropRatioEma,
                stats.LastAckAgeMs);
            _ = LiveVideoStreams.ChangeRecordingQuality(
                Session,
                state,
                info,
                cancellationToken).SuppressExceptions();
        }
    }

    private long ComputeAndUpdateBytesPerSec(VideoSourceKind kind, long currentBytes, CpuTimestamp now)
    {
        if (!_lastEncodedSampleByKind.TryGetValue(kind, out var prev) || prev.At == default) {
            _lastEncodedSampleByKind[kind] = (currentBytes, now);
            return 0;
        }
        var dtSec = (now - prev.At).TotalSeconds;
        _lastEncodedSampleByKind[kind] = (currentBytes, now);
        if (dtSec <= 0) return 0;

        var dBytes = Math.Max(0, currentBytes - prev.BytesAt);
        return (long)(dBytes / dtSec);
    }

    private static double ComputeSenderSignalLevel(double dropRatioEma, double ackAgeMs, double encodeRatioEma)
    {
        var dropPenalty = Math.Clamp(
            (dropRatioEma - Constants.Video.DropOkSender)
                / (Constants.Video.DropBadSender - Constants.Video.DropOkSender),
            0, 1);
        var ackPenalty = ackAgeMs < 0
            ? 0
            : Math.Clamp(
                (ackAgeMs - Constants.Video.AckOkMs)
                    / (Constants.Video.AckBadMs - Constants.Video.AckOkMs),
                0, 1);
        var encPenalty = Math.Clamp(
            (encodeRatioEma - Constants.Video.EncOkRatio)
                / (Constants.Video.EncBadRatio - Constants.Video.EncOkRatio),
            0, 1);
        return 1.0 - Math.Max(dropPenalty, Math.Max(ackPenalty, encPenalty));
    }

    // Nested types

    public sealed record RecordingQualitySnapshot(
        VideoSourceKind Kind,
        RecordingQualityState? State,
        RecorderStats? Health,
        int Signal,
        RecordingQualityReason Reason,
        int? DebugMaxLayerCount);
}
