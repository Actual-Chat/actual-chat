using ActualChat.Streaming;
using ActualChat.UI.Blazor.App.Module;
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
        [StreamKind.Webcam] = new RecordingAggregator(RecordingThresholds.ForKind(StreamKind.Webcam)),
        [StreamKind.Screencast] = new RecordingAggregator(RecordingThresholds.ForKind(StreamKind.Screencast)),
    };
    private readonly Dictionary<StreamKind, RecordingQualityState> _stateByKind = new();
    // Cap applied after the aggregator decision (None|1|2|3 for webcam, None|1|2 for screencast).
    // null = no cap. Diagnostic-only override; aggregator still runs unchanged.
    private readonly Dictionary<StreamKind, int?> _recordingCapByKind = new();
    // Last health/signal/reason per kind, surfaced by GetRecordingSnapshots() to the diagnostics UI.
    private readonly Dictionary<StreamKind, RecorderHealthSnapshot> _lastHealthByKind = new();
    private readonly Dictionary<StreamKind, int> _lastSignalByKind = new();
    private readonly Dictionary<StreamKind, RecordingQualityReason> _lastReasonByKind = new();
    // Most recent recorder reference per kind, captured on PushRecorderHealth.
    // Used to push cap changes immediately (next health push re-clamps anyway).
    private readonly Dictionary<StreamKind, VideoRecorder> _activeRecorderByKind = new();
    // Capacity estimator + last aggregate snapshot for the receiver-side panel.
    // Driven by Tick() which the diagnostics modal calls each poll cycle.
    private static readonly TimeSpan PlaybackHealthTtl = TimeSpan.FromSeconds(10);
    private readonly CapacityEstimator _capacityEstimator = new(PlaybackThresholds.Defaults);
    private readonly Dictionary<StreamId, PlaybackHealthState> _playbackByStream = new();
    private readonly Lock _playbackLock = new();
    private PlaybackQualitySnapshot _playbackSnapshot = PlaybackQualitySnapshot.Empty;
    private PlaybackOverrideMode _playbackOverride = PlaybackOverrideMode.Off;
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
    public async Task PushRecorderHealth(
        StreamKind kind,
        RecorderHealthSnapshot snapshot,
        VideoRecorder recorder,
        CancellationToken cancellationToken)
    {
        _lastHealth = snapshot;
        _lastHealthByKind[kind] = snapshot;
        _activeRecorderByKind[kind] = recorder;
        if (!_recordingByKind.TryGetValue(kind, out var aggregator))
            return;
        if (_coldStartTicksRemaining > 0) {
            _coldStartTicksRemaining--;
            return;
        }
        var thresholds = RecordingThresholds.ForKind(kind);
        var signal = RecordingClassifier.Classify(snapshot, thresholds);
        var decision = aggregator.Step(signal);
        _lastSignalByKind[kind] = signal;
        _lastReasonByKind[kind] = decision.Reason;

        // Apply cap after the aggregator decision. The aggregator's own state is
        // never modified by the cap — clearing the cap restores the un-clamped
        // target on the next push.
        var aggregatorTarget = decision.NewTargetLayerCount;
        var cap = _recordingCapByKind.GetValueOrDefault(kind);
        var effective = cap is { } c ? Math.Min(aggregatorTarget, c) : aggregatorTarget;
        var prevState = _stateByKind.GetValueOrDefault(kind);
        var newState = new RecordingQualityState(aggregatorTarget, effective);
        _stateByKind[kind] = newState;
        var changed = decision.Changed || prevState is null || prevState.EffectiveLayerCount != effective;
        if (!changed)
            return;

        Log.LogWarning(
            "RecordingQuality changed: kind={Kind} target={Target} effective={Effective} cap={Cap} "
            + "reason={Reason} signal={Signal} "
            + "encodeP50={EncP50:F2} encodeP90={EncP90:F2} slotRate={SlotRate:F2} "
            + "backlogMs={Backlog:F0} skips={Skips} ackAgeMs={Ack:F0} connected={Connected}",
            kind, aggregatorTarget, effective, cap, decision.Reason, signal,
            snapshot.EncodeRatioP50, snapshot.EncodeRatioP90, snapshot.SlotReplacementRate,
            snapshot.SenderBacklogP90Ms, snapshot.SenderSkipsPerWindow, snapshot.LastAckAgeMs,
            snapshot.IsConnected);

        await recorder.SetTargetLayerCount(effective, cancellationToken).ConfigureAwait(false);

        var info = new RecordingQualityInfo(decision.Reason, snapshot);
        _ = await LiveVideoStreams.ChangeRecordingQuality(
            Session,
            newState,
            info,
            cancellationToken).ConfigureAwait(false);
    }

    public IReadOnlyList<RecordingQualitySnapshot> GetRecordingSnapshots()
    {
        var result = new List<RecordingQualitySnapshot>(_recordingByKind.Count);
        foreach (var (kind, aggregator) in _recordingByKind) {
            var hasHealth = _lastHealthByKind.TryGetValue(kind, out var health);
            var aggregatorTarget = aggregator.TargetLayerCount;
            var cap = _recordingCapByKind.GetValueOrDefault(kind);
            var effective = cap is { } c ? Math.Min(aggregatorTarget, c) : aggregatorTarget;
            var recorder = _activeRecorderByKind.GetValueOrDefault(kind);
            result.Add(new RecordingQualitySnapshot(
                kind,
                aggregatorTarget,
                effective,
                RecordingThresholds.Defaults.MaxTargetLayerCount,
                cap,
                _lastReasonByKind.GetValueOrDefault(kind, RecordingQualityReason.Stable),
                _lastSignalByKind.GetValueOrDefault(kind),
                hasHealth ? health : null,
                recorder?.MaxSpatialCap ?? int.MaxValue,
                _coldStartTicksRemaining,
                recorder is not null));
        }
        return result;
    }

    public int? GetRecordingCap(StreamKind kind)
        => _recordingCapByKind.GetValueOrDefault(kind);

    public Task SetRecordingCap(StreamKind kind, int? cap, CancellationToken cancellationToken)
    {
        if (cap is { } c && c <= 0)
            cap = null;
        _recordingCapByKind[kind] = cap;
        Log.LogWarning("SetRecordingCap: kind={Kind} cap={Cap}", kind, cap);
        // Immediately re-push the effective target. Next PushRecorderHealth would
        // also re-apply, but applying now avoids waiting up to ~1 s for the
        // diagnostic feedback loop to feel instant.
        if (!_activeRecorderByKind.TryGetValue(kind, out var recorder))
            return Task.CompletedTask;
        if (!_recordingByKind.TryGetValue(kind, out var aggregator))
            return Task.CompletedTask;

        var aggregatorTarget = aggregator.TargetLayerCount;
        var effective = cap is { } cap2 ? Math.Min(aggregatorTarget, cap2) : aggregatorTarget;
        _stateByKind[kind] = new RecordingQualityState(aggregatorTarget, effective);
        return recorder.SetTargetLayerCount(effective, cancellationToken);
    }

    public Task PushPlaybackHealth(StreamId streamId, PlaybackHealthSnapshot snapshot, CancellationToken cancellationToken)
    {
        var verdict = PlaybackVerdictClassifier.Classify(
            snapshot.BufferDurationMsP50,
            snapshot.KeyframeSkipsInWindow,
            PlaybackThresholds.Defaults,
            snapshot.StreamAgeMs,
            snapshot.DecoderQueueDepthP90,
            snapshot.QualityReductionRequested);
        lock (_playbackLock)
            _playbackByStream[streamId] = new PlaybackHealthState(snapshot, verdict, CpuTimestamp.Now);
        var reason = verdict switch {
            < 0 => PlaybackQualityReason.Backoff,
            > 0 => PlaybackQualityReason.Climb,
            _ => PlaybackQualityReason.Stable,
        };
        return RecomputePlaybackQuality(reason, cancellationToken);
    }

    public Task RequestPlaybackQualityReduction(StreamId streamId, CancellationToken cancellationToken)
    {
        lock (_playbackLock) {
            if (_playbackByStream.TryGetValue(streamId, out var state)) {
                var snapshot = state.Snapshot with { QualityReductionRequested = true };
                _playbackByStream[streamId] = state with {
                    Snapshot = snapshot,
                    Verdict = -1,
                    LastSeen = CpuTimestamp.Now,
                };
            }
            else {
                _playbackByStream[streamId] = new PlaybackHealthState(
                    new PlaybackHealthSnapshot(
                        IncomingByteRate: 0,
                        BufferDurationMsP50: 0,
                        KeyframeSkipsInWindow: 0,
                        DecoderQueueDepthP90: Constants.Video.HighBufferDepthThreshold + 1,
                        CurrentMaxSpatial: 2,
                        CurrentMaxTemporal: int.MaxValue,
                        Priority: PlaybackStreamPriority.Primary,
                        StreamAgeMs: int.MaxValue,
                        QualityReductionRequested: true),
                    Verdict: -1,
                    LastSeen: CpuTimestamp.Now);
            }
        }
        return RecomputePlaybackQuality(PlaybackQualityReason.Backoff, cancellationToken);
    }

    public PlaybackOverrideMode PlaybackOverride => _playbackOverride;

    public PlaybackQualitySnapshot GetPlaybackSnapshot()
        => _playbackSnapshot;

    // Called by the diagnostics modal each polling tick. Folds the per-stream
    // diagnostic samples into a CapacityEstimator step + verdict map for the UI.
    // Pure projection — does not push to the server.
    public void TickPlayback(IReadOnlyList<PlaybackStreamInfoLite> streams)
    {
        if (streams.Count == 0) {
            _playbackSnapshot = PlaybackQualitySnapshot.Empty;
            return;
        }

        var verdicts = new Dictionary<string, int>(streams.Count);
        var signals = new List<(long Rate, int Verdict)>(streams.Count);
        long totalBytesPerSec = 0;
        foreach (var s in streams) {
            var verdict = PlaybackVerdictClassifier.Classify(
                s.BufferDurationMsP50,
                s.KeyframeSkipsInWindow,
                PlaybackThresholds.Defaults,
                s.StreamAgeMs);
            verdicts[s.StreamId] = verdict;
            var rate = s.IncomingByteRate;
            signals.Add((rate, verdict));
            totalBytesPerSec += rate;
        }
        var aggregate = AggregateHealth.Compute(signals);
        var capacity = _capacityEstimator.Step(aggregate, totalBytesPerSec);
        _playbackSnapshot = new PlaybackQualitySnapshot(capacity, aggregate, verdicts);
    }

    public Task SetPlaybackOverride(
        PlaybackOverrideMode mode,
        IReadOnlyList<PlaybackOverrideStreamHint> streamHints,
        CancellationToken cancellationToken)
    {
        var changed = _playbackOverride != mode;
        _playbackOverride = mode;
        Log.LogWarning("SetPlaybackOverride: mode={Mode} streams={Count}", mode, streamHints.Count);
        return PushPlaybackOverride(streamHints, changed && mode == PlaybackOverrideMode.Off, cancellationToken);
    }

    public Task RepushPlaybackOverride(
        IReadOnlyList<PlaybackOverrideStreamHint> streamHints,
        CancellationToken cancellationToken)
    {
        if (_playbackOverride == PlaybackOverrideMode.Off)
            return Task.CompletedTask;

        return PushPlaybackOverride(streamHints, releaseAfter: false, cancellationToken);
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
                _capacityEstimator.Reset();
            }
            _wasConnected = isConnected;
        }
    }

    private async Task PushPlaybackOverride(
        IReadOnlyList<PlaybackOverrideStreamHint> streamHints,
        bool releaseAfter,
        CancellationToken cancellationToken)
    {
        var mode = _playbackOverride;
        var requested = mode == PlaybackOverrideMode.Off
            ? null
            : BuildRequestedQuality(mode, streamHints);
        var info = new PlaybackQualityInfo(
            EstimatedCapacityBytesPerSec: _playbackSnapshot.EstimatedCapacityBytesPerSec,
            AggregateHealth: _playbackSnapshot.AggregateHealth,
            Reason: PlaybackQualityReason.ActiveSetChanged,
            IsColdStart: false,
            Streams: new ApiMap<string, PlaybackStreamInfo>());
        _ = await LiveVideoStreams.ChangePlaybackQuality(
            Session, requested, info, cancellationToken).ConfigureAwait(false);
        await UpdateRequestedReceiveQualityRegistry(streamHints, requested, cancellationToken).ConfigureAwait(false);
        if (releaseAfter) {
            // Off transition — push one final null-quality release so the server clears any prior pin.
            _ = await LiveVideoStreams.ChangePlaybackQuality(
                Session, requestedQuality: null, info, cancellationToken).ConfigureAwait(false);
            await ClearRequestedReceiveQualityRegistry(streamHints, cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task UpdateRequestedReceiveQualityRegistry(
        IReadOnlyList<PlaybackOverrideStreamHint> streamHints,
        ApiMap<string, ReceiveQuality>? requested,
        CancellationToken cancellationToken)
    {
        var jsMethod = $"{BlazorUIAppModule.ImportName}.setRequestedReceiveQuality";
        foreach (var hint in streamHints) {
            if (requested is not null && requested.TryGetValue(hint.StreamId, out var q)) {
                await Hub.JS.InvokeVoidAsync(
                    jsMethod, cancellationToken, hint.StreamId, q.MaxSpatialLayer, q.MaxTemporalLayer)
                    .ConfigureAwait(false);
            }
            else {
                await Hub.JS.InvokeVoidAsync(
                    jsMethod, cancellationToken, hint.StreamId, (object?)null, (object?)null)
                    .ConfigureAwait(false);
            }
        }
    }

    private async Task ClearRequestedReceiveQualityRegistry(
        IReadOnlyList<PlaybackOverrideStreamHint> streamHints,
        CancellationToken cancellationToken)
    {
        var jsMethod = $"{BlazorUIAppModule.ImportName}.setRequestedReceiveQuality";
        foreach (var hint in streamHints) {
            await Hub.JS.InvokeVoidAsync(
                jsMethod, cancellationToken, hint.StreamId, (object?)null, (object?)null)
                .ConfigureAwait(false);
        }
    }

    private static ApiMap<string, ReceiveQuality> BuildRequestedQuality(
        PlaybackOverrideMode mode,
        IReadOnlyList<PlaybackOverrideStreamHint> hints)
    {
        var map = new ApiMap<string, ReceiveQuality>();
        foreach (var hint in hints) {
            ReceiveQuality q = mode switch {
                PlaybackOverrideMode.Degrade => ReceiveQuality.Lowest,
                PlaybackOverrideMode.Upgrade => ReceiveQuality.Default,
                PlaybackOverrideMode.Keep => new ReceiveQuality(
                    Math.Max(0, hint.CurrentSpatialLayerId),
                    int.MaxValue),
                _ => ReceiveQuality.Default,
            };
            map[hint.StreamId] = q;
        }
        return map;
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

    private async Task RecomputePlaybackQuality(PlaybackQualityReason reason, CancellationToken cancellationToken)
    {
        var entries = GetFreshPlaybackEntries();
        if (entries.Count == 0) {
            _playbackSnapshot = PlaybackQualitySnapshot.Empty;
            return;
        }

        var signals = entries
            .Select(x => (x.Value.Snapshot.IncomingByteRate, x.Value.Verdict))
            .ToArray();
        var aggregateHealth = AggregateHealth.Compute(signals);
        var sumIncomingBytesPerSec = entries.Sum(x => Math.Max(0, x.Value.Snapshot.IncomingByteRate));
        var capacity = _capacityEstimator.Step(aggregateHealth, sumIncomingBytesPerSec);
        var verdicts = entries.ToDictionary(x => x.Key.Value, x => x.Value.Verdict);
        _playbackSnapshot = new PlaybackQualitySnapshot(capacity, aggregateHealth, verdicts);

        if (_playbackOverride != PlaybackOverrideMode.Off)
            return;

        var primaries = entries
            .Where(x => x.Value.Snapshot.Priority == PlaybackStreamPriority.Primary)
            .Select(ToStreamRequest)
            .ToArray();
        var secondaries = entries
            .Where(x => x.Value.Snapshot.Priority != PlaybackStreamPriority.Primary)
            .Select(ToStreamRequest)
            .ToArray();
        var requested = Allocator.Allocate(capacity, primaries, secondaries);
        var requestedMap = new ApiMap<string, ReceiveQuality>();
        foreach (var (streamId, _) in entries)
            requestedMap[streamId.Value] = requested.GetValueOrDefault(streamId.Value, ReceiveQuality.Lowest);
        var streamInfoMap = new ApiMap<string, PlaybackStreamInfo>();
        foreach (var (streamId, state) in entries) {
            var requestedQuality = requestedMap[streamId.Value];
            streamInfoMap[streamId.Value] = new PlaybackStreamInfo(
                state.Snapshot.IncomingByteRate,
                state.Snapshot.BufferDurationMsP50,
                state.Snapshot.KeyframeSkipsInWindow,
                state.Snapshot.DecoderQueueDepthP90,
                requestedQuality.MaxSpatialLayer,
                requestedQuality.MaxTemporalLayer,
                state.Snapshot.Priority,
                state.Verdict);
        }
        var info = new PlaybackQualityInfo(
            capacity,
            aggregateHealth,
            reason,
            IsColdStart: false,
            streamInfoMap);
        _ = await LiveVideoStreams.ChangePlaybackQuality(
            Session,
            requestedMap,
            info,
            cancellationToken).ConfigureAwait(false);
        await UpdateRequestedReceiveQualityRegistry(
            entries
                .Select(x => new PlaybackOverrideStreamHint(x.Key.Value, x.Value.Snapshot.CurrentMaxSpatial))
                .ToArray(),
            requestedMap,
            cancellationToken).ConfigureAwait(false);

        return;

        static StreamRequest ToStreamRequest(KeyValuePair<StreamId, PlaybackHealthState> entry)
        {
            var top = Math.Max(1, entry.Value.Snapshot.IncomingByteRate);
            var currentSpatial = Math.Max(0, entry.Value.Snapshot.CurrentMaxSpatial);
            var baseRate = currentSpatial <= 0 ? top : Math.Max(1, top / (currentSpatial + 1));
            var maxSpatialLayer = entry.Value.Snapshot.QualityReductionRequested
                ? 0
                : (int?)null;
            return new StreamRequest(entry.Key.Value, baseRate, top, maxSpatialLayer);
        }
    }

    private List<KeyValuePair<StreamId, PlaybackHealthState>> GetFreshPlaybackEntries()
    {
        lock (_playbackLock) {
            var staleStreamIds = _playbackByStream
                .Where(x => x.Value.LastSeen.Elapsed > PlaybackHealthTtl)
                .Select(x => x.Key)
                .ToArray();
            foreach (var streamId in staleStreamIds)
                _playbackByStream.Remove(streamId);
            return _playbackByStream.ToList();
        }
    }

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
            if (decision.Changed) {
                Log.LogWarning(
                    "RecordingQualityTest changed: phase={Phase:F2} signal={Signal} target={Target} reason={Reason}",
                    phase, signal, aggregator.TargetLayerCount, decision.Reason);
                var fakeHealth = new RecorderHealthSnapshot(0, 0, 0, 0, 0, 0, IsConnected: true);
                var info = new RecordingQualityInfo(decision.Reason, fakeHealth);
                _ = LiveVideoStreams.ChangeRecordingQuality(
                    Session, aggregator.Snapshot(), info, CancellationToken.None);
            }
            else
                Log.LogInformation(
                    "RecordingQualityTest: phase={Phase:F2} signal={Signal} target={Target} reason={Reason}",
                    phase, signal, aggregator.TargetLayerCount, decision.Reason);
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
            if (capacity != lastCapacity) {
                Log.LogWarning(
                    "PlaybackQualityTest changed: phase={Phase:F2} signal={Signal} capacity={Capacity} reason={Reason}",
                    phase, signal, capacity, reason);
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
            else
                Log.LogInformation(
                    "PlaybackQualityTest: phase={Phase:F2} signal={Signal} capacity={Capacity} reason={Reason}",
                    phase, signal, capacity, reason);
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
        public static RecordingThresholds Defaults => ForKind(StreamKind.Webcam);

        public static RecordingThresholds ForKind(StreamKind kind)
            => new(
            EncodeRatioBadAbove: 0.8,
            EncodeRatioGoodBelow: 0.5,
            BacklogBadMs: 200,
            BacklogGoodMs: 50,
            LastAckBadMs: 2000,
            LastAckGoodMs: 500,
            SkipsBadCount: 5,
            MinTargetLayerCount: 1,
            MaxTargetLayerCount: kind == StreamKind.Webcam
                ? Constants.Video.WebcamMaxSimulcastTiers
                : Constants.Video.ScreencastMaxSimulcastTiers,
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

    /// <summary>
    /// Diagnostic projection of the per-kind recording quality state for the UI.
    /// </summary>
    public sealed record RecordingQualitySnapshot(
        StreamKind Kind,
        int AggregatorTargetLayerCount,
        int EffectiveLayerCount,
        int MaxLayerCount,
        int? Cap,
        RecordingQualityReason LastReason,
        int LastSignal,
        RecorderHealthSnapshot? LastHealth,
        int ServerMaxSpatialLayer,
        int ColdStartTicksRemaining,
        bool RecorderActive);

    /// <summary>
    /// Diagnostic projection of the receiver-side aggregate quality state.
    /// </summary>
    public sealed record PlaybackQualitySnapshot(
        long EstimatedCapacityBytesPerSec,
        double AggregateHealth,
        IReadOnlyDictionary<string, int> Verdicts)
    {
        public static readonly PlaybackQualitySnapshot Empty = new(0, 0, new Dictionary<string, int>());
    }

    /// <summary>
    /// Inputs for the modal-driven receiver-side <see cref="PlaybackVerdictClassifier"/>
    /// + <see cref="AggregateHealth"/> + <see cref="CapacityEstimator"/> step.
    /// </summary>
    public sealed record PlaybackStreamInfoLite(
        string StreamId,
        long IncomingByteRate,
        int BufferDurationMsP50,
        int KeyframeSkipsInWindow,
        int StreamAgeMs);

    public enum PlaybackOverrideMode { Off, Degrade, Keep, Upgrade }

    /// <summary>
    /// Per-stream hint for <see cref="SetPlaybackOverride"/>: provides the
    /// currently-forwarded layer ID so <see cref="PlaybackOverrideMode.Keep"/>
    /// can pin to it.
    /// </summary>
    public sealed record PlaybackOverrideStreamHint(string StreamId, int CurrentSpatialLayerId);

    // --- Playback branch (Step 10.4) ---

    public sealed record PlaybackThresholds(
        int BufferDurationMsBadBelow,
        int BufferDurationMsTooHighAbove,
        int StartupGraceMs,
        int KeyframeSkipsBadAtOrAbove,
        int DecoderQueueDepthBadAbove,
        long MinCapacityBytesPerSec,
        long ColdStartCapacityBytesPerSec,
        double ClimbCap,
        double BackoffFactor)
    {
        public static PlaybackThresholds Defaults => new(
            BufferDurationMsBadBelow: (int)Math.Round(Constants.Video.TargetBufferDuration.TotalMilliseconds),
            BufferDurationMsTooHighAbove: 400,
            StartupGraceMs: 1000,
            KeyframeSkipsBadAtOrAbove: 1,
            DecoderQueueDepthBadAbove: Constants.Video.HighBufferDepthThreshold,
            MinCapacityBytesPerSec: 50_000,
            ColdStartCapacityBytesPerSec: 1_500_000,
            ClimbCap: 1.4142135623730951,   // √2
            BackoffFactor: 0.7);
    }

    /// <summary>
    /// Pure per-stream classifier: -1 (bad), 0 (neutral), +1 (good)
    /// based on buffer span and keyframe skip count. The ramp-up band is the
    /// intentional decoder buffer target up to the "too much buffered" ceiling.
    /// Low buffer is ignored during initial startup grace; over-buffering is
    /// neutral because it indicates playback/decoder lag, not low bandwidth.
    /// </summary>
    public static class PlaybackVerdictClassifier
    {
        public static int Classify(
            int bufferDurationMsP50,
            int keyframeSkipsInWindow,
            PlaybackThresholds t,
            int streamAgeMs = int.MaxValue,
            int decoderQueueDepthP90 = 0,
            bool qualityReductionRequested = false)
        {
            if (qualityReductionRequested)
                return -1;
            if (keyframeSkipsInWindow >= t.KeyframeSkipsBadAtOrAbove)
                return -1;
            if (decoderQueueDepthP90 > t.DecoderQueueDepthBadAbove)
                return -1;
            if (bufferDurationMsP50 < t.BufferDurationMsBadBelow)
                return streamAgeMs < t.StartupGraceMs ? 0 : -1;
            if (bufferDurationMsP50 <= t.BufferDurationMsTooHighAbove)
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

    public sealed record StreamRequest(
        string StreamId,
        long PredictedRateAtBase,
        long PredictedRateAtTop,
        int? MaxSpatialLayer = null);

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
            IReadOnlyList<StreamRequest> secondaries,
            int? maxSpatialLayer = null)
        {
            var result = new Dictionary<string, ReceiveQuality>();
            var remaining = budgetBytesPerSec;
            foreach (var req in primaries) {
                var effectiveMaxSpatial = MinSpatialLayer(maxSpatialLayer, req.MaxSpatialLayer);
                var topQuality = effectiveMaxSpatial is { } max
                    ? new ReceiveQuality(Math.Max(0, max), int.MaxValue)
                    : ReceiveQuality.Default;
                var predictedTopRate = effectiveMaxSpatial is <= 0
                    ? req.PredictedRateAtBase
                    : req.PredictedRateAtTop;
                if (remaining >= predictedTopRate) {
                    result[req.StreamId] = topQuality;
                    remaining -= predictedTopRate;
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

            static int? MinSpatialLayer(int? globalMax, int? streamMax)
                => (globalMax, streamMax) switch {
                    ({ } g, { } s) => Math.Min(g, s),
                    ({ } g, null) => g,
                    (null, { } s) => s,
                    _ => null,
                };
        }
    }
    public sealed record PlaybackHealthSnapshot(
        long IncomingByteRate,
        int BufferDurationMsP50,
        int KeyframeSkipsInWindow,
        int DecoderQueueDepthP90,
        int CurrentMaxSpatial,
        int CurrentMaxTemporal,
        PlaybackStreamPriority Priority,
        int StreamAgeMs,
        bool QualityReductionRequested = false);

    private sealed record PlaybackHealthState(
        PlaybackHealthSnapshot Snapshot,
        int Verdict,
        CpuTimestamp LastSeen);
}
