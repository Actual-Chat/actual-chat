using ActualChat.Bandwidth;
using ActualChat.Rpc;
using ActualChat.Streaming;
using ActualChat.UI.Blazor.App.Module;

namespace ActualChat.UI.Blazor.App.Services;

public sealed partial class VideoQualityUI
{
    private static readonly string JSGetDebugSettingsMethod = $"{BlazorUIAppModule.ImportName}.getVideoDebugSettings";

    private int? _debugMaxRecordingLayerCount;
    private int? _debugMaxPlaybackLayerCount;
    private double _debugBandwidthMultiplier = 1.0;
    private CancellationTokenSource? _recordingTestCts;
    private CancellationTokenSource? _playbackTestCts;

    public async Task SetDebugMaxRecordingLayerCount(int? layerCount, CancellationToken cancellationToken)
    {
        layerCount = NormalizeLayerCount(layerCount);
        if (_debugMaxRecordingLayerCount == layerCount)
            return;

        _debugMaxRecordingLayerCount = layerCount;
        await RunOutboundTick(cancellationToken).ConfigureAwait(false);
    }

    public async Task SetDebugMaxPlaybackLayerCount(
        int? layerCount,
        IReadOnlyList<PlaybackStreamHint> streamHints,
        CancellationToken cancellationToken)
    {
        layerCount = NormalizeLayerCount(layerCount);
        var changed = _debugMaxPlaybackLayerCount != layerCount;
        _debugMaxPlaybackLayerCount = layerCount;

        if (GetFreshPlaybackEntries().Count != 0) {
            await RecomputePlaybackQuality(PlaybackQualityReason.ActiveSetChanged, cancellationToken).ConfigureAwait(false);
            return;
        }

        if (streamHints.Count == 0) {
            if (changed)
                _playbackSnapshot = PlaybackQualitySnapshot.Empty;
            return;
        }

        var requested = BuildLayerCapQuality(layerCount, streamHints);
        var info = new PlaybackQualityInfo(
            EstimatedCapacityBytesPerSec: _playbackSnapshot.EstimatedCapacityBytesPerSec,
            AggregateHealth: _playbackSnapshot.AggregateHealth,
            Reason: PlaybackQualityReason.ActiveSetChanged,
            IsColdStart: false,
            Streams: new ApiMap<string, PlaybackStreamInfo>());
        _ = LiveVideoStreams.ChangePlaybackQuality(
            Session, requested, info, cancellationToken).SuppressExceptions();
        if (layerCount is null)
            await ClearRequestedReceiveQualityRegistry(streamHints, cancellationToken).ConfigureAwait(false);
        else
            await UpdateRequestedReceiveQualityRegistry(streamHints, requested, cancellationToken).ConfigureAwait(false);
    }

    public void SetDebugBandwidthMultiplier(double value)
    {
        if (!double.IsFinite(value) || value <= 0)
            value = 1.0;
        _debugBandwidthMultiplier = value;
    }

    /// <summary>
    /// Drives the recording quality controller with a synthetic ternary signal
    /// for <paramref name="periodSeconds"/> seconds. Signal pattern per cycle:
    /// 5% at 0 → 45% at -1 → 5% at 0 → 45% at +1 (10% total at neutral).
    /// </summary>
    public void BeginRecordingQualityTest(int periodSeconds)
    {
        var period = TimeSpan.FromSeconds(Math.Max(1, periodSeconds));
        var oldCts = Interlocked.Exchange(ref _recordingTestCts, new CancellationTokenSource(period));
        oldCts.CancelAndDisposeSilently();
        var ct = _recordingTestCts.Token;
        Log.LogWarning("BeginRecordingQualityTest: period={Period}s", period.TotalSeconds);
        _ = BackgroundTask.Run(
            () => RunRecordingTest(period, ct),
            Log,
            "BeginRecordingQualityTest failed",
            CancellationToken.None);
    }

    /// <summary>
    /// Drives the playback capacity estimator with a synthetic ternary signal
    /// (same pattern as the recording test). Pushes
    /// <see cref="ILiveVideoStreams.ChangePlaybackQuality"/> with info-only
    /// payloads (qualityByStream = null) so the test never affects live
    /// stream allocation.
    /// </summary>
    public void BeginPlaybackQualityTest(int periodSeconds)
    {
        var period = TimeSpan.FromSeconds(Math.Max(1, periodSeconds));
        var oldCts = Interlocked.Exchange(ref _playbackTestCts, new CancellationTokenSource(period));
        oldCts.CancelAndDisposeSilently();
        var ct = _playbackTestCts.Token;
        Log.LogWarning("BeginPlaybackQualityTest: period={Period}s", period.TotalSeconds);
        _ = BackgroundTask.Run(
            () => RunPlaybackTest(period, ct),
            Log,
            "BeginPlaybackQualityTest failed",
            CancellationToken.None);
    }

    // Private methods

    private async Task LoadDebugSettings(CancellationToken cancellationToken)
    {
        var settings = await JS
            .InvokeAsync<VideoDebugSettings>(JSGetDebugSettingsMethod, cancellationToken)
            .ConfigureAwait(false);
        _debugMaxRecordingLayerCount = NormalizeLayerCount(settings.MaxOutboundLayerCount);
        _debugMaxPlaybackLayerCount = NormalizeLayerCount(settings.MaxInboundLayerCount);
    }

    private async Task RunRecordingTest(TimeSpan period, CancellationToken ct)
    {
        var estimator = new BandwidthEstimator(
            new BandwidthEstimatorConfig(Constants.Video.InitialOutboundCeilingBps));
        var conn = new RpcConnectionInfo(1, SystemClock.Now);
        var startedAt = CpuTimestamp.Now;
        while (!ct.IsCancellationRequested) {
            var elapsed = startedAt.Elapsed;
            if (elapsed >= period)
                break;
            var phase = (elapsed.TotalSeconds % period.TotalSeconds) / period.TotalSeconds;
            var signalLevel = TestSignalLevel(phase);
            estimator.Tick(conn, SystemClock.Now, currentBandwidthBps: 500_000, signalLevel);
            Log.LogInformation(
                "RecordingQualityTest: phase={Phase:F2} signalLevel={Signal:F2} ceiling={Ceiling} verdict={Verdict}",
                phase, signalLevel, estimator.CeilingBps, estimator.LastVerdict);
            try {
                await Task.Delay(TimeSpan.FromSeconds(1), ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException) {
                break;
            }
        }
        Log.LogWarning("RecordingQualityTest: done");
    }

    private async Task RunPlaybackTest(TimeSpan period, CancellationToken ct)
    {
        var estimator = new BandwidthEstimator(
            new BandwidthEstimatorConfig(Constants.Video.InitialInboundCeilingBps));
        var conn = new RpcConnectionInfo(1, SystemClock.Now);
        var startedAt = CpuTimestamp.Now;
        var lastCeiling = -1L;
        while (!ct.IsCancellationRequested) {
            var elapsed = startedAt.Elapsed;
            if (elapsed >= period)
                break;
            var phase = (elapsed.TotalSeconds % period.TotalSeconds) / period.TotalSeconds;
            var signalLevel = TestSignalLevel(phase);
            estimator.Tick(conn, SystemClock.Now, currentBandwidthBps: 1_000_000, signalLevel);
            var reason = signalLevel < 0.85
                ? PlaybackQualityReason.Backoff
                : signalLevel >= 0.95
                    ? PlaybackQualityReason.Climb
                    : PlaybackQualityReason.Stable;
            if (estimator.CeilingBps != lastCeiling) {
                Log.LogWarning(
                    "PlaybackQualityTest changed: phase={Phase:F2} signal={Signal:F2} ceiling={Ceiling} reason={Reason}",
                    phase, signalLevel, estimator.CeilingBps, reason);
                var info = new PlaybackQualityInfo(
                    estimator.CeilingBps,
                    AggregateHealth: 2 * signalLevel - 1,
                    Reason: reason,
                    IsColdStart: false,
                    Streams: new ApiMap<string, PlaybackStreamInfo>());
                _ = LiveVideoStreams.ChangePlaybackQuality(
                    Session, qualityByStream: null, info, CancellationToken.None).SuppressExceptions();
                lastCeiling = estimator.CeilingBps;
            }
            else
                Log.LogInformation(
                    "PlaybackQualityTest: phase={Phase:F2} signal={Signal:F2} ceiling={Ceiling}",
                    phase, signalLevel, estimator.CeilingBps);
            try {
                await Task.Delay(TimeSpan.FromSeconds(1), ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException) {
                break;
            }
        }
        Log.LogWarning("PlaybackQualityTest: done");
    }

    // Synthetic signal — produces a 0.90 → 0.30 → 0.90 → 1.0 sweep over a
    // single cycle. ~10% of time is spent near a polarity flip (5% each).
    private static double TestSignalLevel(double phase)
    {
        if (phase < 0.05) return 0.90;
        if (phase < 0.50) return 0.30;
        if (phase < 0.55) return 0.90;
        return 1.0;
    }

    // Nested types

    public sealed record VideoDebugSettings(
        bool ForceFloorCodecOnly,
        int? MaxOutboundLayerCount,
        int? MaxInboundLayerCount);
}
