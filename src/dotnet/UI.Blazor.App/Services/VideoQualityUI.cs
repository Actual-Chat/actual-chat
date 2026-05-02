using ActualChat.Streaming;
using ActualChat.UI.Blazor.Services;
using ActualLab.Interception;

namespace ActualChat.UI.Blazor.App.Services;

/// <summary>
/// Client-side video quality controller. Drives recording (this file) and
/// playback (Step 10.4) decisions from local health signals; pushes summaries
/// to the server via <see cref="ILiveVideoStreams.ChangeRecordingQuality"/>
/// and <see cref="ILiveVideoStreams.ChangePlaybackQuality"/>.
/// </summary>
public sealed class VideoQualityUI : UIWorkerBase<AppUIHub>, INotifyInitialized
{
    private readonly Dictionary<StreamKind, RecordingAggregator> _recordingByKind = new() {
        [StreamKind.Webcam] = new RecordingAggregator(RecordingThresholds.Defaults),
        [StreamKind.Screencast] = new RecordingAggregator(RecordingThresholds.Defaults),
    };
    private RecorderHealthSnapshot? _lastHealth;
    private bool _wasConnected = true;
    private int _coldStartTicksRemaining;
    private CancellationTokenSource? _recordingTestCts;
    private CancellationTokenSource? _playbackTestCts;

    private ConnectivityUI ConnectivityUI => Hub.ConnectivityUI;
    private ILiveVideoStreams LiveVideoStreams
        => field ??= Services.GetRequiredService<ILiveVideoStreams>();
    private Session Session => Hub.Session;

    public VideoQualityUI(AppUIHub hub) : base(hub) { }

    public void Initialized()
        => this.Start();

    /// <summary>
    /// Receives a <see cref="RecorderHealthSnapshot"/> from the worker
    /// (JS-side aggregator in <c>video-processing.ts</c>). Triggers a
    /// classification + aggregation step for the matching <see cref="StreamKind"/>.
    /// </summary>
    public void PushRecorderHealth(StreamKind kind, RecorderHealthSnapshot snapshot)
    {
        _lastHealth = snapshot;
        if (!_recordingByKind.TryGetValue(kind, out var aggregator))
            return;
        if (_coldStartTicksRemaining > 0) {
            _coldStartTicksRemaining--;
            return;
        }
        var signal = RecordingClassifier.Classify(snapshot, RecordingThresholds.Defaults);
        var decision = aggregator.Step(signal);
        if (!decision.Changed)
            return;

        var info = new RecordingQualityInfo(decision.Reason, snapshot);
        _ = LiveVideoStreams.ChangeRecordingQuality(
            Session,
            aggregator.Snapshot(),
            info,
            CancellationToken.None);
    }

    protected override async Task OnRun(CancellationToken cancellationToken)
    {
        // Watch ConnectivityUI transitions to apply cold-start grace on
        // false→true edges (signal windows wiped on reconnect).
        var cState = ConnectivityUI.IsConnected.Computed;
        await foreach (var (isConnected, _) in cState.Changes(cancellationToken).ConfigureAwait(false)) {
            if (!_wasConnected && isConnected) {
                foreach (var aggregator in _recordingByKind.Values)
                    aggregator.Reset();
                _coldStartTicksRemaining = ColdStartTicks;
            }
            _wasConnected = isConnected;
        }
    }

    private const int ColdStartTicks = 2; // ~2 s of grace at 1 Hz

    // --- Debug / test entry points ---

    /// <summary>
    /// Drives the recording quality controller with a synthetic ternary signal
    /// for <paramref name="periodSeconds"/> seconds. Signal pattern per cycle:
    /// 5% at 0 → 45% at -1 → 5% at 0 → 45% at +1 (10% total at neutral).
    /// Uses a fresh aggregator with fast thresholds (no cooldown, K=1) so the
    /// target layer count walks the full range within the period. Pushes
    /// <see cref="ILiveVideoStreams.ChangeRecordingQuality"/> on each step.
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
    /// payloads (requestedQuality = null) so the test never affects live
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

    private async Task RunRecordingTest(TimeSpan period, CancellationToken ct)
    {
        // Fast thresholds: no cooldown, K=1 — every -1 steps down, every +1
        // steps up. The aggregator's floor / ceiling guards still hold.
        var thresholds = RecordingThresholds.Defaults with {
            ConsecutiveGoodForClimb = 1,
            CooldownTicksAfterBackoff = 0,
        };
        var aggregator = new RecordingAggregator(thresholds);
        var startedAt = CpuTimestamp.Now;
        while (!ct.IsCancellationRequested) {
            var elapsed = startedAt.Elapsed;
            if (elapsed >= period)
                break;
            var phase = (elapsed.TotalSeconds % period.TotalSeconds) / period.TotalSeconds;
            var signal = TestSignal(phase);
            var decision = aggregator.Step(signal);
            Log.LogInformation(
                "RecordingQualityTest: phase={Phase:F2} signal={Signal} target={Target} reason={Reason}",
                phase, signal, aggregator.TargetLayerCount, decision.Reason);
            if (decision.Changed) {
                var fakeHealth = new RecorderHealthSnapshot(0, 0, 0, 0, 0, 0, IsConnected: true);
                var info = new RecordingQualityInfo(decision.Reason, fakeHealth);
                _ = LiveVideoStreams.ChangeRecordingQuality(
                    Session, aggregator.Snapshot(), info, CancellationToken.None);
            }
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
        var estimator = new CapacityEstimator(PlaybackThresholds.Defaults);
        var startedAt = CpuTimestamp.Now;
        var lastCapacity = -1L;
        while (!ct.IsCancellationRequested) {
            var elapsed = startedAt.Elapsed;
            if (elapsed >= period)
                break;
            var phase = (elapsed.TotalSeconds % period.TotalSeconds) / period.TotalSeconds;
            var signal = TestSignal(phase);
            var capacity = estimator.Step(signal, sumIncomingBytesPerSec: 1_000_000);
            var reason = signal switch {
                < 0 => PlaybackQualityReason.Backoff,
                > 0 => PlaybackQualityReason.Climb,
                _ => PlaybackQualityReason.Stable,
            };
            Log.LogInformation(
                "PlaybackQualityTest: phase={Phase:F2} signal={Signal} capacity={Capacity}",
                phase, signal, capacity);
            if (capacity != lastCapacity) {
                var info = new PlaybackQualityInfo(
                    capacity,
                    AggregateHealth: signal,
                    Reason: reason,
                    IsColdStart: false,
                    Streams: new ApiMap<string, PlaybackStreamInfo>());
                _ = LiveVideoStreams.ChangePlaybackQuality(
                    Session, requestedQuality: null, info, CancellationToken.None);
                lastCapacity = capacity;
            }
            try {
                await Task.Delay(TimeSpan.FromSeconds(1), ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException) {
                break;
            }
        }
        Log.LogWarning("PlaybackQualityTest: done");
    }

    // Synthetic signal — produces [0, -1, ..., -1, 0, +1, ..., +1] over a
    // single cycle. ~10% of time is spent at neutral (5% at each polarity flip).
    internal static int TestSignal(double phase)
    {
        if (phase < 0.05) return 0;
        if (phase < 0.50) return -1;
        if (phase < 0.55) return 0;
        return 1;
    }

    // Nested types

    public sealed record RecordingThresholds(
        double EncodeRatioBadAbove,
        double EncodeRatioGoodBelow,
        double BacklogBadMs,
        double BacklogGoodMs,
        double LastAckBadMs,
        double LastAckGoodMs,
        double SkipsBadCount,
        int MinTargetLayerCount,
        int MaxTargetLayerCount,
        int ConsecutiveGoodForClimb,
        int CooldownTicksAfterBackoff)
    {
        public static RecordingThresholds Defaults => new(
            EncodeRatioBadAbove: 0.8,
            EncodeRatioGoodBelow: 0.5,
            BacklogBadMs: 200,
            BacklogGoodMs: 50,
            LastAckBadMs: 2000,
            LastAckGoodMs: 500,
            SkipsBadCount: 5,
            MinTargetLayerCount: 1,
            MaxTargetLayerCount: 4,
            ConsecutiveGoodForClimb: 5,
            CooldownTicksAfterBackoff: 5);
    }

    /// <summary>
    /// Pure ternary classifier: returns +1 (good), 0 (neutral), or -1 (bad)
    /// from a single <see cref="RecorderHealthSnapshot"/>.
    /// </summary>
    public static class RecordingClassifier
    {
        public static int Classify(RecorderHealthSnapshot h, RecordingThresholds t)
        {
            if (!h.IsConnected)
                return 0;

            var anyBad =
                h.EncodeRatioP90 > t.EncodeRatioBadAbove
                || h.SenderBacklogP90Ms > t.BacklogBadMs
                || (h.LastAckAgeMs >= 0 && h.LastAckAgeMs > t.LastAckBadMs)
                || h.SenderSkipsPerWindow >= t.SkipsBadCount;
            if (anyBad)
                return -1;

            var allGood =
                h.EncodeRatioP90 < t.EncodeRatioGoodBelow
                && h.SenderBacklogP90Ms < t.BacklogGoodMs
                && (h.LastAckAgeMs < 0 || h.LastAckAgeMs < t.LastAckGoodMs)
                && h.SenderSkipsPerWindow == 0;
            return allGood ? 1 : 0;
        }
    }

    /// <summary>
    /// AIMD aggregator — instant step-down on a single bad signal,
    /// step-up only after K consecutive good signals, with a cooldown
    /// after step-down to prevent flapping.
    /// </summary>
    public sealed class RecordingAggregator(RecordingThresholds thresholds)
    {
        private int _targetLayerCount = thresholds.MaxTargetLayerCount;
        private int _consecutiveGood;
        private int _cooldownLeft;

        public int TargetLayerCount => _targetLayerCount;

        public void Reset()
        {
            _targetLayerCount = thresholds.MaxTargetLayerCount;
            _consecutiveGood = 0;
            _cooldownLeft = 0;
        }

        public RecordingDecision Step(int signal)
        {
            // -1 = backoff, +1 = climb candidate, 0 = hold
            if (signal < 0) {
                if (_targetLayerCount <= thresholds.MinTargetLayerCount) {
                    _consecutiveGood = 0;
                    return new RecordingDecision(_targetLayerCount, false, RecordingQualityReason.StuckAtFloor);
                }
                _targetLayerCount--;
                _consecutiveGood = 0;
                _cooldownLeft = thresholds.CooldownTicksAfterBackoff;
                return new RecordingDecision(_targetLayerCount, true, RecordingQualityReason.Backoff);
            }

            if (_cooldownLeft > 0) {
                _cooldownLeft--;
                _consecutiveGood = 0;
                return new RecordingDecision(_targetLayerCount, false, RecordingQualityReason.Stable);
            }

            if (signal > 0) {
                _consecutiveGood++;
                if (_consecutiveGood >= thresholds.ConsecutiveGoodForClimb
                    && _targetLayerCount < thresholds.MaxTargetLayerCount) {
                    _targetLayerCount++;
                    _consecutiveGood = 0;
                    return new RecordingDecision(_targetLayerCount, true, RecordingQualityReason.Climb);
                }
            }
            else {
                _consecutiveGood = 0;
            }
            return new RecordingDecision(_targetLayerCount, false, RecordingQualityReason.Stable);
        }

        public RecordingQualityState Snapshot()
            => new(_targetLayerCount, _targetLayerCount);
    }

    public sealed record RecordingDecision(
        int NewTargetLayerCount,
        bool Changed,
        RecordingQualityReason Reason);

    // --- Playback branch (Step 10.4) ---

    public sealed record PlaybackThresholds(
        int BufferDurationMsBadBelow,
        int BufferDurationMsGoodAbove,
        int KeyframeSkipsBadAtOrAbove,
        long MinCapacityBytesPerSec,
        long ColdStartCapacityBytesPerSec,
        double ClimbCap,
        double BackoffFactor)
    {
        public static PlaybackThresholds Defaults => new(
            BufferDurationMsBadBelow: 100,
            BufferDurationMsGoodAbove: 400,
            KeyframeSkipsBadAtOrAbove: 1,
            MinCapacityBytesPerSec: 50_000,
            ColdStartCapacityBytesPerSec: 1_500_000,
            ClimbCap: 1.4142135623730951,   // √2
            BackoffFactor: 0.7);
    }

    /// <summary>
    /// Pure per-stream classifier: -1 (bad), 0 (neutral), +1 (good)
    /// based on buffer span and keyframe skip count.
    /// </summary>
    public static class PlaybackVerdictClassifier
    {
        public static int Classify(int bufferDurationMsP50, int keyframeSkipsInWindow, PlaybackThresholds t)
        {
            if (keyframeSkipsInWindow >= t.KeyframeSkipsBadAtOrAbove)
                return -1;
            if (bufferDurationMsP50 < t.BufferDurationMsBadBelow)
                return -1;
            if (bufferDurationMsP50 >= t.BufferDurationMsGoodAbove)
                return 1;
            return 0;
        }
    }

    /// <summary>
    /// Byte-weighted aggregate of per-stream verdicts. A single small lagging
    /// stream paired with a healthy big stream stays near 0 (most bandwidth
    /// is healthy); a big lagging stream paired with a small healthy one
    /// trends to -1 (most bandwidth is unhealthy).
    /// </summary>
    public static class AggregateHealth
    {
        public static double Compute(IReadOnlyList<(long Rate, int Verdict)> streamSignals)
        {
            if (streamSignals.Count == 0)
                return 0;
            long totalRate = 0;
            double weighted = 0;
            foreach (var (rate, verdict) in streamSignals) {
                var w = rate <= 0 ? 1 : rate;
                totalRate += w;
                weighted += w * verdict;
            }
            return totalRate <= 0 ? 0 : weighted / totalRate;
        }
    }

    /// <summary>
    /// AIMD-style capacity estimator. On overall good aggregate (≥ +0.5),
    /// climb the capacity ceiling toward sqrt(2)× of the actual incoming rate.
    /// On overall bad aggregate (≤ -0.5), back off to 0.7× of the current
    /// capacity. Otherwise hold.
    /// </summary>
    public sealed class CapacityEstimator(PlaybackThresholds thresholds)
    {
        private long _capacity = thresholds.ColdStartCapacityBytesPerSec;

        public long Capacity => _capacity;

        public void Reset()
            => _capacity = thresholds.ColdStartCapacityBytesPerSec;

        public long Step(double aggregateHealth, long sumIncomingBytesPerSec)
        {
            const double goodThreshold = 0.5;
            const double badThreshold = -0.5;
            if (aggregateHealth <= badThreshold) {
                _capacity = (long)(_capacity * thresholds.BackoffFactor);
            }
            else if (aggregateHealth >= goodThreshold && sumIncomingBytesPerSec > 0) {
                var climbCeiling = (long)(sumIncomingBytesPerSec * thresholds.ClimbCap);
                if (climbCeiling > _capacity)
                    _capacity = climbCeiling;
            }
            if (_capacity < thresholds.MinCapacityBytesPerSec)
                _capacity = thresholds.MinCapacityBytesPerSec;
            return _capacity;
        }
    }

    public sealed record StreamRequest(string StreamId, long PredictedRateAtBase, long PredictedRateAtTop);

    /// <summary>
    /// Greedy budget allocator: primaries get top spatial first, in their
    /// list order; secondaries fill the remainder in their list order.
    /// Streams that don't fit at the base layer are dropped from the result —
    /// the caller maps that to <see cref="ReceiveQuality.Lowest"/>.
    /// </summary>
    public static class Allocator
    {
        public static IReadOnlyDictionary<string, ReceiveQuality> Allocate(
            long budgetBytesPerSec,
            IReadOnlyList<StreamRequest> primaries,
            IReadOnlyList<StreamRequest> secondaries)
        {
            var result = new Dictionary<string, ReceiveQuality>();
            var remaining = budgetBytesPerSec;
            foreach (var req in primaries) {
                if (remaining >= req.PredictedRateAtTop) {
                    result[req.StreamId] = ReceiveQuality.Default;
                    remaining -= req.PredictedRateAtTop;
                }
                else if (remaining >= req.PredictedRateAtBase) {
                    result[req.StreamId] = new ReceiveQuality(0, int.MaxValue);
                    remaining -= req.PredictedRateAtBase;
                }
            }
            foreach (var req in secondaries) {
                if (remaining >= req.PredictedRateAtBase) {
                    result[req.StreamId] = new ReceiveQuality(0, int.MaxValue);
                    remaining -= req.PredictedRateAtBase;
                }
            }
            return result;
        }
    }
}
