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
    // Separate sample tracker for wire-acked (delivered) bytes — its delta over
    // time under backlog is the measured uplink drain rate.
    private readonly Dictionary<VideoSourceKind, (long BytesAt, CpuTimestamp At)> _lastAckedSampleByKind = new();
    // WireQueueDepthEma above this (bundles) is treated as a sustained backlog:
    // the link can't drain instantly, so the acked-bytes rate equals capacity.
    private const double WireBacklogBundles = 2.0;
    private readonly Dictionary<VideoSourceKind, int> _lastAppliedTargetByKind = new();
    private readonly Dictionary<VideoSourceKind, int> _lastAppliedFpsCeilingByKind = new();
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
    private readonly ThermalCap _thermalCap;
    private readonly SpeculativeProbe _outboundProbe = new();
    private readonly BandwidthEstimator _outboundBwEstimator;
    // One classifier per recording run. The Encoder/Uplink streak counters
    // are persisted across ticks; restart-cooldown branches do NOT recreate
    // it — see RunOutboundTick for the streak-reset path.
    private readonly SenderHealthClassifier _senderHealthClassifier = new();
    private EncoderHealth _lastEncoderHealth = EncoderHealth.Empty;
    private UplinkHealth _lastUplinkHealth = UplinkHealth.Empty;
    private CpuTimestamp _outboundStartedAt;
    private CpuTimestamp _outboundLastEvalAt;
    private volatile bool _forceOutboundEval;

    public EncoderHealth OutboundEncoderHealth => _lastEncoderHealth;
    public UplinkHealth OutboundUplinkHealth => _lastUplinkHealth;

    public BandwidthEstimator OutboundBandwidthEstimator => _outboundBwEstimator;
    public LayerCap OutboundEncodingLayers => _outboundEncodingCap.Layers;
    public LayerCap OutboundBandwidthLayers => _outboundBandwidthCap.Layers;
    public ThermalCap ThermalCap => _thermalCap;
    public int OutboundDeviceCameraCap => _outboundLayers.DeviceCameraCap;
    public int OutboundDeviceScreencastCap => _outboundLayers.ScreencastCap;
    // Current effective outbound camera cap (soft-started, ramped by QC). openGate
    // opens at this, not the device ceiling, so the top tier is earned by the ramp.
    public int OutboundCameraCap
        => Math.Min(_outboundEncodingCap.Layers.CameraLayers, _outboundBandwidthCap.Layers.CameraLayers);

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
            _lastAckedSampleByKind[kind] = (snapshot.WireAckedBytes, CpuTimestamp.Now);
            _outboundBwEstimator.ResetStreaks();
            _outboundEncodingCap.ResetStreaks();
            _outboundBandwidthCap.ResetStreaks();
            _outboundProbe.Reset();
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
        // still in restart cooldown, skip this tick entirely. Decrement
        // ALL positive cooldown entries (not just the current `kind`):
        // if the restarted kind stops producing stats — or simply isn't
        // the one this callback is for — its entry would otherwise stay
        // pinned forever and lock outbound QC out on every subsequent tick.
        if (IsAnyKindInRestartCooldown()) {
            DecrementAndPruneRestartCooldowns();
            _lastRecordingReasonByKind[kind] = RecordingQualityReason.ColdStartTick;
            return;
        }

        if (_outboundStartedAt == default)
            _outboundStartedAt = CpuTimestamp.Now;
        // A thermal transition forces the next tick to evaluate immediately —
        // escalation must clamp the encoder now, not after the 5 s QC interval.
        var forced = _forceOutboundEval;
        if (!forced && !IsEvaluationDue(_outboundStartedAt, _outboundLastEvalAt)) {
            _lastRecordingReasonByKind[kind] = RecordingQualityReason.Stable;
            return;
        }
        _forceOutboundEval = false;
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

    private void DecrementAndPruneRestartCooldowns()
    {
        List<VideoSourceKind>? toRemove = null;
        foreach (var (k, ticks) in _restartCooldownByKind) {
            if (ticks <= 0)
                continue;
            var next = ticks - 1;
            if (next <= 0) {
                toRemove ??= new List<VideoSourceKind>(1);
                toRemove.Add(k);
            }
            else
                _restartCooldownByKind[k] = next;
        }
        if (toRemove != null) {
            foreach (var k in toRemove)
                _restartCooldownByKind.Remove(k);
        }
    }

    public RecordingQualitySnapshot GetRecordingSnapshot(VideoSourceKind kind)
    {
        var state = _lastRecordingStateByKind.GetValueOrDefault(kind);
        var isScreencast = kind == VideoSourceKind.ScreenCast;
        var encLayers = isScreencast
            ? _outboundEncodingCap.Layers.ScreencastLayers
            : _outboundEncodingCap.Layers.CameraLayers;
        var bwLayers = isScreencast
            ? _outboundBandwidthCap.Layers.ScreencastLayers
            : _outboundBandwidthCap.Layers.CameraLayers;
        return new(
            kind,
            state,
            _lastRecorderStatsByKind.GetValueOrDefault(kind),
            _lastRecordingSignalByKind.GetValueOrDefault(kind),
            _lastRecordingReasonByKind.GetValueOrDefault(kind),
            _debugMaxRecordingLayerCount,
            encLayers,
            bwLayers,
            _outboundEncodingCap.GoodStreak,
            _outboundEncodingCap.BadStreak,
            _outboundBwEstimator.PositiveStreak,
            _outboundBwEstimator.NegativeStreak,
            _outboundBwEstimator.CeilingBps * 8 / 1000.0,
            _outboundBwEstimator.LastCurrentBps * 8 / 1000.0);
    }

    // Private methods

    private async Task RunOutboundTick(CancellationToken cancellationToken)
    {
        // Per the split-attribution plan: aggregate stats across kinds, then
        // classify into Encoder + Uplink health. Each verdict drives exactly
        // one cap — encoder pressure NEVER leaks into BWE here.
        var fusedDropRatio = 0.0;
        var fusedAckAgeMs = -1.0;
        var fusedEncodeDeficit = 0.0;
        var fusedEncodeQueueDepth = 0.0;
        var fusedWireQueueDepth = 0.0;
        var fusedFloodSkipPerSec = 0.0;
        var maxRestartStreak = 0;
        var maxPeerReconnectStreak = 0;
        var isTabBackgrounded = false;
        long totalBytesPerSec = 0;
        long ackedBytesPerSec = 0;
        var sampleAt = CpuTimestamp.Now;
        foreach (var (k, stats) in _lastRecorderStatsByKind) {
            fusedDropRatio = Math.Max(fusedDropRatio, stats.SenderFrameDropRatioEma);
            if (stats.LastAckAgeMs >= 0)
                fusedAckAgeMs = Math.Max(fusedAckAgeMs, stats.LastAckAgeMs);
            fusedEncodeDeficit = Math.Max(fusedEncodeDeficit, stats.EncodeDeficitEma);
            if (stats.EncodeQueueDepthEma >= 0)
                fusedEncodeQueueDepth = Math.Max(fusedEncodeQueueDepth, stats.EncodeQueueDepthEma);
            if (stats.WireQueueDepthEma >= 0)
                fusedWireQueueDepth = Math.Max(fusedWireQueueDepth, stats.WireQueueDepthEma);
            fusedFloodSkipPerSec = Math.Max(fusedFloodSkipPerSec, stats.FloodGateSkipPerSec);
            maxRestartStreak = Math.Max(maxRestartStreak, stats.EncoderRestartStreakIn60s);
            maxPeerReconnectStreak = Math.Max(maxPeerReconnectStreak, stats.PeerReconnectStreak);
            isTabBackgrounded = isTabBackgrounded || stats.IsTabBackgrounded;
            totalBytesPerSec += ComputeAndUpdateBytesPerSec(_lastEncodedSampleByKind, k, stats.BytesEncoded, sampleAt);
            ackedBytesPerSec += ComputeAndUpdateBytesPerSec(_lastAckedSampleByKind, k, stats.WireAckedBytes, sampleAt);
        }

        var encoderHealth = _senderHealthClassifier.ClassifyEncoder(
            encodeDeficitEma: fusedEncodeDeficit,
            encodeQueueDepthEma: fusedEncodeQueueDepth,
            restartStreakIn60s: maxRestartStreak,
            senderEncodePathDropRatio: 0, // TODO: split RecorderStats.dropTrace into encode-stage vs wire-stage.
            isTabBackgrounded: isTabBackgrounded);
        var uplinkHealth = _senderHealthClassifier.ClassifyUplink(
            wireLastAckAgeMs: fusedAckAgeMs,
            wireQueueDepthEma: fusedWireQueueDepth,
            floodGateSkipPerSec: fusedFloodSkipPerSec,
            peerReconnectStreak: maxPeerReconnectStreak,
            senderWirePathDropRatio: fusedDropRatio);
        _lastEncoderHealth = encoderHealth;
        _lastUplinkHealth = uplinkHealth;
        // OpenTelemetry emission for these verdicts is deferred: AppMeters
        // lives in Core.Server (not referenced from UI.Blazor.App). When
        // RecordingQualityInfo grows the per-leg fields, the server-side
        // ChangeRecordingQuality handler will record them.

        // Uplink-only signal feeds BWE. Verdict→continuous mapping: Bad=0,
        // everything else=1. Streak hysteresis inside the classifier
        // is the smoothing; BWE no longer averages raw penalties.
        var uplinkSignal = VerdictToSignal(uplinkHealth.Verdict);
        var connection = ConnectivityUI.ConnectionInfo.Value;
        _outboundBwEstimator.Tick(connection, SystemClock.Now, totalBytesPerSec, uplinkSignal);
        // Drain-rate measurement under wire backlog: the acked-bytes rate is the
        // capacity — but only when it lags production (the estimator rejects the
        // downward anchor otherwise: a backlog draining at the produce rate is a
        // credit-window/RTT stall, not a link bottleneck).
        if (fusedWireQueueDepth >= WireBacklogBundles && ackedBytesPerSec > 0)
            _outboundBwEstimator.ApplyMeasuredCapacity(ackedBytesPerSec, totalBytesPerSec, SystemClock.Now);
        var preEncCam = _outboundEncodingCap.Layers.CameraLayers;
        var preBwCam = _outboundBandwidthCap.Layers.CameraLayers;
        // EncodingCap still consumes the raw encode ratio. The classifier
        // verdict is used for observability + future bg/fg threshold switch.
        _outboundEncodingCap.Tick(fusedEncodeDeficit);
        _outboundBandwidthCap.Tick(_outboundBwEstimator);
        var postEncCam = _outboundEncodingCap.Layers.CameraLayers;
        var postBwCam = _outboundBandwidthCap.Layers.CameraLayers;
        if (preEncCam != postEncCam || preBwCam != postBwCam)
            Log.LogInformation(
                "RunOutboundTick: cap changed — encCam {PreEnc}->{PostEnc} (encVerdict={EncVerdict}, encRatio={EncRatio:F2}, " +
                "badStreak={BadStreak}, goodStreak={GoodStreak}), " +
                "bwCam {PreBw}->{PostBw} (uplinkVerdict={UplinkVerdict}, uplinkSignal={Signal:F2}, " +
                "bweVerdict={BweVerdict}, ceiling={CeilingBps}bps, current={CurrentBps}bps, " +
                "negStreak={NegStreak}, posStreak={PosStreak}), totalBps={TotalBps}, dropRatio={DropRatio:F3}, ackAgeMs={AckAgeMs:F0}",
                preEncCam, postEncCam, encoderHealth.Verdict, fusedEncodeDeficit,
                _outboundEncodingCap.BadStreak, _outboundEncodingCap.GoodStreak,
                preBwCam, postBwCam, uplinkHealth.Verdict, uplinkSignal,
                _outboundBwEstimator.LastVerdict,
                _outboundBwEstimator.CeilingBps, _outboundBwEstimator.LastCurrentBps,
                _outboundBwEstimator.NegativeStreak, _outboundBwEstimator.PositiveStreak,
                totalBytesPerSec * 8, fusedDropRatio, fusedAckAgeMs);

        var encLayers = _outboundEncodingCap.Layers;
        var bwLayers = _outboundBandwidthCap.Layers;

        // Speculative probe (camera): when bandwidth is the binding cap and below
        // the device max on a healthy, non-backlogged link, send one extra layer
        // for a short window to test for uplink headroom the drain-rate path can't
        // measure (no backlog). A passed probe commits by bumping bwLayers.
        var backlogged = fusedWireQueueDepth >= WireBacklogBundles;
        var baseCamera = Math.Min(encLayers.CameraLayers, bwLayers.CameraLayers);
        var canGrow = baseCamera < bwLayers.DeviceCameraCap
            && baseCamera == bwLayers.CameraLayers
            && !backlogged;
        var cameraProbeExtra = _outboundProbe.Tick(
            canGrow, fusedAckAgeMs, fusedWireQueueDepth, bwLayers, _recordersByKind.Keys);

        // Recompute after a possible commit, then add the in-window probe layer.
        var effCamera = Math.Min(
            bwLayers.DeviceCameraCap,
            Math.Min(encLayers.CameraLayers, bwLayers.CameraLayers) + cameraProbeExtra);
        var effScreencast = Math.Min(encLayers.ScreencastLayers, bwLayers.ScreencastLayers);
        _thermalCap.Tick(SystemClock.Now, ThermalTracker.Level.Value);
        effCamera = Math.Min(effCamera, _thermalCap.CameraLayers);
        effScreencast = Math.Min(effScreencast, _thermalCap.ScreencastLayers);
        if (_debugMaxRecordingLayerCount is { } debugCap) {
            effCamera = Math.Min(effCamera, debugCap);
            effScreencast = Math.Min(effScreencast, debugCap);
        }

        foreach (var (k, recorder) in _recordersByKind) {
            var target = k == VideoSourceKind.Camera ? effCamera : effScreencast;
            target = Math.Max(1, target);
            await ApplyOutboundTarget(k, recorder, target, cancellationToken).ConfigureAwait(false);
            await ApplyFpsCeiling(k, recorder, _thermalCap.MaxFps, cancellationToken).ConfigureAwait(false);
        }

        var capChange = "";
        if (preEncCam != postEncCam)
            capChange = $"camera {preEncCam}→{postEncCam} (enc)";
        else if (preBwCam != postBwCam)
            capChange = $"camera {preBwCam}→{postBwCam} (bw)";
        var ceilingKbps = _outboundBwEstimator.CeilingBps * 8 / 1000;
        var currentKbps = _outboundBwEstimator.LastCurrentBps * 8 / 1000;
        var reason = encoderHealth.Verdict == HealthVerdict.Bad
            ? $"encDeficit {(fusedEncodeDeficit * 100):F0}%"
            : uplinkHealth.Verdict == HealthVerdict.Bad
                ? $"ack {fusedAckAgeMs:F0}ms / drop {fusedDropRatio:F2}"
                : _outboundBwEstimator.LastVerdict switch {
                    BandwidthVerdict.Good => $"BW ↑ {ceilingKbps} kbps",
                    BandwidthVerdict.Bad => $"BW ↓ {ceilingKbps} kbps",
                    _ => "stable",
                };
        var rawA = $"def={(fusedEncodeDeficit * 100):F0}% q={fusedEncodeQueueDepth:F1} rs={maxRestartStreak}";
        var rawB = $"ack={(fusedAckAgeMs < 0 ? "n/a" : fusedAckAgeMs.ToString("F0") + "ms")} drop={fusedDropRatio:F2} qd={fusedWireQueueDepth:F1} flood={fusedFloodSkipPerSec:F1}";
        var rawBw = $"{(_outboundBwEstimator.LastVerdict == BandwidthVerdict.Good ? "↑" : _outboundBwEstimator.LastVerdict == BandwidthVerdict.Bad ? "↓" : "=")}{ceilingKbps}/cur {currentKbps} kbps";
        AppendOutboundDecision(new QualityDecisionEntry(
            SystemClock.Now,
            encoderHealth.Verdict,
            uplinkHealth.Verdict,
            _outboundBwEstimator.LastVerdict,
            capChange,
            reason,
            rawA,
            rawB,
            rawBw));
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
                stats.LastAckAgeMs,
                _thermalCap.Level,
                stats.IsHardwareAccelerated);
            _ = LiveVideoStreams.ChangeRecordingQuality(
                Session,
                state,
                info,
                cancellationToken).SuppressExceptions();
        }
    }

    private async Task ApplyFpsCeiling(
        VideoSourceKind kind,
        VideoRecorder recorder,
        int maxFps,
        CancellationToken cancellationToken)
    {
        if (_lastAppliedFpsCeilingByKind.GetValueOrDefault(kind) == maxFps)
            return;

        _lastAppliedFpsCeilingByKind[kind] = maxFps;
        await recorder.SetFpsCeiling(maxFps, cancellationToken).ConfigureAwait(false);
    }

    private static long ComputeAndUpdateBytesPerSec(
        Dictionary<VideoSourceKind, (long BytesAt, CpuTimestamp At)> samples,
        VideoSourceKind kind, long currentBytes, CpuTimestamp now)
    {
        if (!samples.TryGetValue(kind, out var prev) || prev.At == default) {
            samples[kind] = (currentBytes, now);
            return 0;
        }
        var dtSec = (now - prev.At).TotalSeconds;
        samples[kind] = (currentBytes, now);
        if (dtSec <= 0) return 0;

        var dBytes = Math.Max(0, currentBytes - prev.BytesAt);
        return (long)(dBytes / dtSec);
    }

    // Map a classifier verdict to a continuous BWE signal. Marginal/Unknown
    // are treated as Good (1.0): the classifier already imposes a 2-tick
    // streak before declaring Bad, so anything below Bad is conservatively
    // not-a-problem. The earlier 0.5 mapping caused BWE.Tick to interpret
    // every warm-up tick as < BadThreshold (0.85) → spurious cap walks
    // before the first real signal arrived.
    private static double VerdictToSignal(HealthVerdict verdict)
        => verdict == HealthVerdict.Bad ? 0.0 : 1.0;

    // Nested types

    public sealed record RecordingQualitySnapshot(
        VideoSourceKind Kind,
        RecordingQualityState? State,
        RecorderStats? Health,
        int Signal,
        RecordingQualityReason Reason,
        int? DebugMaxLayerCount,
        int EncodeCapLayers,
        int BandwidthCapLayers,
        int EncodeGoodStreak,
        int EncodeBadStreak,
        int BwPositiveStreak,
        int BwNegativeStreak,
        double CeilingKbps,
        double CurrentKbps);
}
