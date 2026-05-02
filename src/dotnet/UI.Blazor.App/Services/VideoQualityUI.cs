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
}
