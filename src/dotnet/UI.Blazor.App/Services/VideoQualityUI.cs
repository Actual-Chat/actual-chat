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
public sealed class VideoQualityUI(AppUIHub hub) : UIWorkerBase<AppUIHub>(hub), INotifyInitialized
{
    private const int ColdStartTicks = 2; // ~2 s of grace at 1 Hz
    private static readonly TimeSpan PlaybackHealthTtl = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan PlaybackQualityKeepAlivePeriod = TimeSpan.FromMinutes(1);
    private static readonly string JSGetDebugSettingsMethod = $"{BlazorUIAppModule.ImportName}.getVideoDebugSettings";

    private readonly Dictionary<StreamKind, RecordingAggregator> _recordingByKind = new() {
        [StreamKind.Webcam] = new RecordingAggregator(RecordingThresholds.Defaults),
        [StreamKind.Screencast] = new RecordingAggregator(RecordingThresholds.Defaults),
    };
    private readonly Dictionary<StreamKind, VideoRecorder> _recordersByKind = new();
    private readonly Dictionary<StreamKind, RecorderHealthSnapshot> _lastRecordingHealthByKind = new();
    private readonly Dictionary<StreamKind, RecordingQualityState> _lastRecordingStateByKind = new();
    private readonly Dictionary<StreamKind, int> _lastRecordingSignalByKind = new();
    private readonly Dictionary<StreamKind, RecordingQualityReason> _lastRecordingReasonByKind = new();
    private readonly Dictionary<StreamId, PlaybackHealthState> _playbackByStream = new();
    private readonly CapacityEstimator _playbackEstimator = new(PlaybackThresholds.Defaults);
    private readonly Lock _playbackLock = new();
    private PlaybackQualitySnapshot _playbackSnapshot = PlaybackQualitySnapshot.Empty;
    private PlaybackOverrideMode _playbackOverride = PlaybackOverrideMode.Off;
    private int? _debugMaxRecordingLayerCount;
    private int? _debugMaxPlaybackLayerCount;
    private bool _wasConnected = true;
    private int _coldStartTicksRemaining;
    private CancellationTokenSource? _recordingTestCts;
    private CancellationTokenSource? _playbackTestCts;

    private ConnectivityUI ConnectivityUI => Hub.ConnectivityUI;
    private ILiveVideoStreams LiveVideoStreams
        => field ??= Services.GetRequiredService<ILiveVideoStreams>();
    private new Session Session => Hub.Session;

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
        _recordersByKind[kind] = recorder;
        _lastRecordingHealthByKind[kind] = snapshot;
        if (!_recordingByKind.TryGetValue(kind, out var aggregator))
            return;
        if (_coldStartTicksRemaining > 0) {
            _coldStartTicksRemaining--;
            _lastRecordingSignalByKind[kind] = 0;
            _lastRecordingReasonByKind[kind] = RecordingQualityReason.ColdStartTick;
            return;
        }
        var signal = RecordingClassifier.Classify(snapshot, RecordingThresholds.Defaults);
        var decision = aggregator.Step(signal);
        _lastRecordingSignalByKind[kind] = signal;
        _lastRecordingReasonByKind[kind] = decision.Reason;
        if (!decision.Changed && _debugMaxRecordingLayerCount is null)
            return;

        if (decision.Changed)
            Log.LogWarning(
                "RecordingQuality changed: kind={Kind} target={Target} reason={Reason} signal={Signal} "
                + "encodeAvg={EncAvg:F2} encodeP90={EncP90:F2} slotRate={SlotRate:F2} "
                + "senderDropRatio={DropRatio:F2} ackAgeMs={Ack:F0} connected={Connected}",
                kind, decision.NewTargetLayerCount, decision.Reason, signal,
                snapshot.EncodeRatioAvg, snapshot.EncodeRatioP90, snapshot.SlotReplacementRate,
                snapshot.SenderFrameDropRatio, snapshot.LastAckAgeMs,
                snapshot.IsConnected);

        await ApplyRecordingQuality(
            kind,
            aggregator,
            recorder,
            decision.Reason,
            snapshot,
            force: decision.Changed,
            cancellationToken).ConfigureAwait(false);
    }

    public Task PushPlaybackHealth(
        StreamId streamId,
        StreamKind streamKind,
        PlaybackHealthSnapshot snapshot,
        CancellationToken cancellationToken)
    {
        var verdict = PlaybackVerdictClassifier.Classify(
            snapshot.BufferDurationMsP50,
            snapshot.KeyframeSkipsInWindow,
            PlaybackThresholds.Defaults,
            snapshot.StreamAgeMs,
            snapshot.DecoderQueueDepthP90,
            snapshot.QualityReductionRequested);
        lock (_playbackLock)
            _playbackByStream[streamId] = new PlaybackHealthState(streamKind, snapshot, verdict, CpuTimestamp.Now);
        var reason = verdict switch {
            < 0 => PlaybackQualityReason.Backoff,
            > 0 => PlaybackQualityReason.Climb,
            _ => PlaybackQualityReason.Stable,
        };
        return RecomputePlaybackQuality(reason, cancellationToken);
    }

    public Task RequestPlaybackQualityReduction(
        StreamId streamId,
        StreamKind streamKind,
        CancellationToken cancellationToken)
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
                    streamKind,
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

    public async Task SetDebugMaxRecordingLayerCount(int? layerCount, CancellationToken cancellationToken)
    {
        layerCount = NormalizeLayerCount(layerCount);
        if (_debugMaxRecordingLayerCount == layerCount)
            return;

        _debugMaxRecordingLayerCount = layerCount;
        foreach (var (kind, aggregator) in _recordingByKind) {
            if (!_recordersByKind.TryGetValue(kind, out var recorder))
                continue;
            if (!_lastRecordingHealthByKind.TryGetValue(kind, out var snapshot))
                continue;

            await ApplyRecordingQuality(
                kind,
                aggregator,
                recorder,
                RecordingQualityReason.Stable,
                snapshot,
                force: true,
                cancellationToken).ConfigureAwait(false);
        }
    }

    public async Task SetDebugMaxPlaybackLayerCount(int? layerCount, CancellationToken cancellationToken)
        => await SetDebugMaxPlaybackLayerCount(layerCount, [], cancellationToken).ConfigureAwait(false);

    public async Task SetDebugMaxPlaybackLayerCount(
        int? layerCount,
        IReadOnlyList<PlaybackOverrideStreamHint> streamHints,
        CancellationToken cancellationToken)
    {
        layerCount = NormalizeLayerCount(layerCount);
        var changed = _debugMaxPlaybackLayerCount != layerCount;
        _debugMaxPlaybackLayerCount = layerCount;

        if (_playbackOverride != PlaybackOverrideMode.Off)
            return;

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

    public PlaybackOverrideMode PlaybackOverride => _playbackOverride;

    public PlaybackQualitySnapshot PlaybackSnapshot => _playbackSnapshot;

    public RecordingQualitySnapshot GetRecordingSnapshot(StreamKind kind)
    {
        var state = _lastRecordingStateByKind.GetValueOrDefault(kind);
        if (state is null && _recordingByKind.TryGetValue(kind, out var aggregator)) {
            var effectiveLayerCount = ApplyLayerCountConstraint(
                aggregator.TargetLayerCount,
                _debugMaxRecordingLayerCount);
            state = aggregator.Snapshot(effectiveLayerCount);
        }
        return new(
            kind,
            state,
            _lastRecordingHealthByKind.GetValueOrDefault(kind),
            _lastRecordingSignalByKind.GetValueOrDefault(kind),
            _lastRecordingReasonByKind.GetValueOrDefault(kind),
            _debugMaxRecordingLayerCount);
    }

    public Task SetPlaybackOverride(
        PlaybackOverrideMode mode,
        IReadOnlyList<PlaybackOverrideStreamHint> streamHints,
        CancellationToken cancellationToken)
    {
        _playbackOverride = mode;
        Log.LogWarning("SetPlaybackOverride: mode={Mode} streams={Count}", mode, streamHints.Count);
        return PushPlaybackOverride(streamHints, cancellationToken);
    }

    public Task RepushPlaybackOverride(
        IReadOnlyList<PlaybackOverrideStreamHint> streamHints,
        CancellationToken cancellationToken)
    {
        if (_playbackOverride == PlaybackOverrideMode.Off)
            return Task.CompletedTask;

        return PushPlaybackOverride(streamHints, cancellationToken);
    }

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

    // Protected methods

    protected override async Task OnRun(CancellationToken cancellationToken)
    {
        await LoadDebugSettings(cancellationToken).ConfigureAwait(false);
        _ = BackgroundTask.Run(
            () => RunPlaybackQualityKeepAlive(cancellationToken),
            Log,
            "Video playback quality keep-alive failed",
            cancellationToken);

        // Watch ConnectivityUI transitions to apply cold-start grace on
        // false→true edges (signal windows wiped on reconnect).
        var cState = ConnectivityUI.IsConnected.Computed;
        await foreach (var (isConnected, _) in cState.Changes(cancellationToken).ConfigureAwait(false)) {
            if (!_wasConnected && isConnected) {
                foreach (var aggregator in _recordingByKind.Values)
                    aggregator.Reset();
                _coldStartTicksRemaining = ColdStartTicks;
                _playbackEstimator.Reset();
            }
            _wasConnected = isConnected;
        }
    }

    // Private methods

    private async Task RunPlaybackQualityKeepAlive(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested) {
            await Task.Delay(PlaybackQualityKeepAlivePeriod, cancellationToken).ConfigureAwait(false);
            var entries = GetFreshPlaybackEntries();
            if (entries.Count == 0)
                continue;

            if (_playbackOverride == PlaybackOverrideMode.Off)
                await RecomputePlaybackQuality(PlaybackQualityReason.Stable, cancellationToken).ConfigureAwait(false);
            else
                await PushPlaybackOverride(ToStreamHints(entries), cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task LoadDebugSettings(CancellationToken cancellationToken)
    {
        try {
            var settings = await Hub.JS.InvokeAsync<VideoDebugSettings>(
                    JSGetDebugSettingsMethod,
                    cancellationToken)
                .ConfigureAwait(false);
            _debugMaxRecordingLayerCount = NormalizeLayerCount(settings.MaxOutboundLayerCount);
            _debugMaxPlaybackLayerCount = NormalizeLayerCount(settings.MaxInboundLayerCount);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
        catch (Exception e) {
            Log.LogDebug(e, "LoadDebugSettings failed");
        }
    }

    private static IReadOnlyList<PlaybackOverrideStreamHint> ToStreamHints(
        IReadOnlyList<KeyValuePair<StreamId, PlaybackHealthState>> entries)
        => entries
            .Select(x => new PlaybackOverrideStreamHint(x.Key.Value, x.Value.Snapshot.CurrentMaxSpatial))
            .ToArray();

    private async Task ApplyRecordingQuality(
        StreamKind kind,
        RecordingAggregator aggregator,
        VideoRecorder recorder,
        RecordingQualityReason reason,
        RecorderHealthSnapshot snapshot,
        bool force,
        CancellationToken cancellationToken)
    {
        var targetLayerCount = aggregator.TargetLayerCount;
        var effectiveLayerCount = ApplyLayerCountConstraint(targetLayerCount, _debugMaxRecordingLayerCount);
        var state = aggregator.Snapshot(effectiveLayerCount);
        var oldState = _lastRecordingStateByKind.GetValueOrDefault(kind);
        if (!force && oldState == state)
            return;

        if (force || oldState?.EffectiveLayerCount != effectiveLayerCount)
            await recorder.SetTargetLayerCount(effectiveLayerCount, cancellationToken).ConfigureAwait(false);

        _lastRecordingStateByKind[kind] = state;
        _lastRecordingReasonByKind[kind] = reason;
        var info = new RecordingQualityInfo(reason, snapshot);
        _ = LiveVideoStreams.ChangeRecordingQuality(
            Session,
            state,
            info,
            cancellationToken).SuppressExceptions();
    }

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
        var capacity = _playbackEstimator.Step(aggregateHealth, sumIncomingBytesPerSec);
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
        var maxSpatialLayer = _debugMaxPlaybackLayerCount is { } maxLayerCount
            ? maxLayerCount - 1
            : (int?)null;
        var requested = Allocator.Allocate(capacity, primaries, secondaries, maxSpatialLayer);
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
        _ = LiveVideoStreams.ChangePlaybackQuality(
            Session,
            requestedMap,
            info,
            cancellationToken).SuppressExceptions();
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
                : DefaultPlaybackMaxSpatialLayer(entry.Value.StreamKind);
            return new StreamRequest(entry.Key.Value, baseRate, top, maxSpatialLayer);
        }
    }

    private static int DefaultPlaybackMaxSpatialLayer(StreamKind streamKind)
        => streamKind == StreamKind.Screencast
            ? Constants.Video.ScreencastMaxSimulcastTiers - 1
            : Math.Max(0, Constants.Video.WebcamMaxSimulcastTiers - 2);

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

    private static int? NormalizeLayerCount(int? layerCount)
        => layerCount is >= 1 and <= 3 ? layerCount : null;

    private async Task PushPlaybackOverride(
        IReadOnlyList<PlaybackOverrideStreamHint> streamHints,
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
        _ = LiveVideoStreams.ChangePlaybackQuality(
            Session, requested, info, cancellationToken).SuppressExceptions();
        await UpdateRequestedReceiveQualityRegistry(streamHints, requested, cancellationToken).ConfigureAwait(false);
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

    private static ApiMap<string, ReceiveQuality> BuildLayerCapQuality(
        int? layerCount,
        IReadOnlyList<PlaybackOverrideStreamHint> hints)
    {
        var map = new ApiMap<string, ReceiveQuality>();
        var quality = layerCount is { } count
            ? new ReceiveQuality(Math.Max(0, count - 1), int.MaxValue)
            : ReceiveQuality.Default;
        foreach (var hint in hints)
            map[hint.StreamId] = quality;
        return map;
    }

    private static int ApplyLayerCountConstraint(int layerCount, int? maxLayerCount)
        => maxLayerCount is { } max ? Math.Clamp(layerCount, 1, max) : layerCount;

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
                var fakeHealth = new RecorderHealthSnapshot(0, 0, 0, 0, 0, IsConnected: true);
                var info = new RecordingQualityInfo(decision.Reason, fakeHealth);
                _ = LiveVideoStreams.ChangeRecordingQuality(
                    Session, aggregator.Snapshot(), info, CancellationToken.None).SuppressExceptions();
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
                    Session, qualityByStream: null, info, CancellationToken.None).SuppressExceptions();
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

    // Nested types

    // Synthetic signal — produces [0, -1, ..., -1, 0, +1, ..., +1] over a
    // single cycle. ~10% of time is spent at neutral (5% at each polarity flip).
    internal static int TestSignal(double phase)
    {
        if (phase < 0.05) return 0;
        if (phase < 0.50) return -1;
        if (phase < 0.55) return 0;
        return 1;
    }

    public sealed record VideoDebugSettings(
        bool ForceH264Only,
        int? MaxOutboundLayerCount,
        int? MaxInboundLayerCount);

    public sealed record PlaybackQualitySnapshot(
        long EstimatedCapacityBytesPerSec,
        double AggregateHealth,
        IReadOnlyDictionary<string, int> Verdicts)
    {
        public static readonly PlaybackQualitySnapshot Empty = new(
            EstimatedCapacityBytesPerSec: 0,
            AggregateHealth: 0,
            Verdicts: new Dictionary<string, int>());
    }

    public sealed record RecordingQualitySnapshot(
        StreamKind Kind,
        RecordingQualityState? State,
        RecorderHealthSnapshot? Health,
        int Signal,
        RecordingQualityReason Reason,
        int? DebugMaxLayerCount);

    public enum PlaybackOverrideMode { Off, Degrade, Keep, Upgrade }

    public sealed record PlaybackOverrideStreamHint(string StreamId, int CurrentSpatialLayerId);

    public sealed record RecordingThresholds(
        double EncodeRatioBadAbove,
        double EncodeRatioGoodBelow,
        double LastAckBadMs,
        double LastAckGoodMs,
        double SenderFrameDropRatioBadAbove,
        int MinTargetLayerCount,
        int MaxTargetLayerCount,
        int ConsecutiveGoodForClimb,
        int CooldownTicksAfterBackoff)
    {
        public static RecordingThresholds Defaults => new(
            EncodeRatioBadAbove: 1.0,
            EncodeRatioGoodBelow: 0.33,
            LastAckBadMs: 2000,
            LastAckGoodMs: 500,
            SenderFrameDropRatioBadAbove: 0.20,
            MinTargetLayerCount: 1,
            MaxTargetLayerCount: Constants.Video.WebcamMaxSimulcastTiers,
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
                h.EncodeRatioAvg > t.EncodeRatioBadAbove
                || (h.LastAckAgeMs >= 0 && h.LastAckAgeMs > t.LastAckBadMs)
                || h.SenderFrameDropRatio > t.SenderFrameDropRatioBadAbove;
            if (anyBad)
                return -1;

            var allGood =
                h.EncodeRatioAvg < t.EncodeRatioGoodBelow
                && (h.LastAckAgeMs < 0 || h.LastAckAgeMs < t.LastAckGoodMs)
                && h.SenderFrameDropRatio == 0;
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

        public RecordingQualityState Snapshot(int? effectiveLayerCount = null)
            => new(_targetLayerCount, effectiveLayerCount ?? _targetLayerCount);
    }

    public sealed record RecordingDecision(
        int NewTargetLayerCount,
        bool Changed,
        RecordingQualityReason Reason);

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
        StreamKind StreamKind,
        PlaybackHealthSnapshot Snapshot,
        int Verdict,
        CpuTimestamp LastSeen);
}
