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
    // Stream-age-tiered evaluation cadence for both rec and playback QC.
    // Health snapshots arrive at 1 Hz; we throttle the controller's
    // decide+push step on top of that to avoid thrash while a fresh stream
    // is still settling and to cut steady-state traffic later. The 5 s
    // startup cooldown covers the L2-keyframe wait (~3 s) plus EMA(10)
    // ramp-up so the first eval lands on a settled buffer signal.
    private static readonly TimeSpan QcStartupCooldown = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan QcSettlingInterval = TimeSpan.FromSeconds(3);
    private static readonly TimeSpan QcSettlingDuration = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan QcSteadyInterval = TimeSpan.FromSeconds(5);

    // Per-stream peak-rate decay. The allocator reads the peak observed
    // incoming byte rate so it doesn't underestimate upper-tier
    // cost when the receiver is currently subscribed to a lower simulcast
    // tier (current rate ≈ baseRate then, but the real top rate is many
    // times higher — without peak tracking, allocator climbs prematurely
    // and oscillates). Peak decays slowly so a sender that genuinely
    // lowered its top bitrate is eventually forgiven; 0.97/s halves in
    // ~23 s, allowing a probe-back-to-top roughly every minute when the
    // capacity estimator catches up.
    private const double PeakDecayPerSecond = 0.97;
    private static readonly string JSGetDebugSettingsMethod = $"{BlazorUIAppModule.ImportName}.getVideoDebugSettings";

    private readonly Dictionary<VideoSourceKind, RecordingAggregator> _recordingByKind = new() {
        [VideoSourceKind.Camera] = new RecordingAggregator(RecordingThresholds.Defaults),
        [VideoSourceKind.ScreenCast] = new RecordingAggregator(RecordingThresholds.Defaults),
    };
    private readonly Dictionary<VideoSourceKind, VideoRecorder> _recordersByKind = new();
    private readonly Dictionary<VideoSourceKind, RecorderHealthSnapshot> _lastRecordingHealthByKind = new();
    private readonly Dictionary<VideoSourceKind, RecordingQualityState> _lastRecordingStateByKind = new();
    private readonly Dictionary<VideoSourceKind, int> _lastRecordingSignalByKind = new();
    private readonly Dictionary<VideoSourceKind, RecordingQualityReason> _lastRecordingReasonByKind = new();
    // Stream-age-aware throttle on QC decisions: cooldown for the first
    // StartupCooldown, then evaluate at SettlingInterval until SettlingDuration
    // of stream age, then SteadyInterval. Prevents premature thrash and keeps
    // steady-state ChangeQuality traffic low.
    private readonly Dictionary<VideoSourceKind, CpuTimestamp> _recordingStartedAt = new();
    private readonly Dictionary<VideoSourceKind, CpuTimestamp> _recordingLastEvalAt = new();
    private readonly Dictionary<StreamId, CpuTimestamp> _playbackStartedAt = new();
    private readonly Dictionary<StreamId, CpuTimestamp> _playbackLastEvalAt = new();
    private readonly Dictionary<StreamId, PlaybackHealthState> _playbackByStream = new();
    private readonly CapacityEstimator _playbackEstimator = new(PlaybackThresholds.Defaults);
    private readonly Lock _playbackLock = new();
    private PlaybackQualitySnapshot _playbackSnapshot = PlaybackQualitySnapshot.Empty;
    private int? _debugMaxRecordingLayerCount;
    private int? _debugMaxPlaybackLayerCount;
    private double _debugBandwidthMultiplier = 1.0;
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
    /// classification + aggregation step for the matching <see cref="VideoSourceKind"/>.
    /// </summary>
    public async Task PushRecorderHealth(
        VideoSourceKind kind,
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
        _lastRecordingSignalByKind[kind] = signal;
        // Stream-age-tiered eval gate. We still record the classifier signal
        // for diagnostics, but the AIMD step only advances on eligible ticks
        // — so the controller doesn't react to single-tick noise during
        // startup or thrash in steady state.
        if (!_recordingStartedAt.ContainsKey(kind))
            _recordingStartedAt[kind] = CpuTimestamp.Now;
        if (!IsEvaluationDue(_recordingStartedAt[kind], _recordingLastEvalAt.GetValueOrDefault(kind))) {
            _lastRecordingReasonByKind[kind] = RecordingQualityReason.Stable;
            return;
        }
        _recordingLastEvalAt[kind] = CpuTimestamp.Now;
        var decision = aggregator.Step(signal);
        _lastRecordingReasonByKind[kind] = decision.Reason;
        if (!decision.Changed && _debugMaxRecordingLayerCount is null)
            return;

        if (decision.Changed)
            Log.LogWarning(
                "RecordingQuality changed: kind={Kind} target={Target} reason={Reason} signal={Signal} "
                + "encodeEma={EncEma:F2} encodeP90={EncP90:F2} slotRateEma={SlotRateEma:F2} "
                + "senderDropRatioEma={DropRatioEma:F2} ackAgeMs={Ack:F0} "
                + "connected={Connected} peerConnected={PeerConnected}",
                kind, decision.NewTargetLayerCount, decision.Reason, signal,
                snapshot.EncodeRatioEma, snapshot.EncodeRatioP90, snapshot.SlotReplacementRateEma,
                snapshot.SenderFrameDropRatioEma, snapshot.LastAckAgeMs,
                snapshot.IsConnected, snapshot.IsPeerConnected);

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
        VideoSourceKind sourceKind,
        PlaybackHealthSnapshot snapshot,
        CancellationToken cancellationToken)
    {
        var verdict = PlaybackVerdictClassifier.Classify(
            snapshot.BufferSpanMsEma,
            PlaybackThresholds.Defaults,
            snapshot.QualityReductionRequested);
        bool isFirstTick;
        lock (_playbackLock) {
            var prev = _playbackByStream.GetValueOrDefault(streamId);
            snapshot = WithRenderFallback(snapshot, prev);
            var peak = ComputeDecayedPeak(prev, snapshot.IncomingByteRate);
            var desiredSize = GetDesiredVideoSize(snapshot, sourceKind);
            var requestedMaxLayerId = snapshot.QualityReductionRequested
                ? 0
                : GetBestLayerFor(sourceKind, desiredSize);
            _playbackByStream[streamId] = new PlaybackHealthState(
                sourceKind, snapshot, verdict, CpuTimestamp.Now, peak,
                requestedMaxLayerId, desiredSize);
            isFirstTick = prev is null;
            if (isFirstTick || !_playbackStartedAt.ContainsKey(streamId))
                _playbackStartedAt[streamId] = CpuTimestamp.Now;
        }
        // First tick always emits an allocation using the current render-size
        // hint, falling back to top size until layout arrives. Subsequent
        // health-driven recomputes are gated by stream-age cadence so we don't
        // thrash during startup or in steady state. Manual paths — override,
        // debug, keep-alive, reduction request — bypass the gate and call
        // RecomputePlaybackQuality directly.
        if (!isFirstTick) {
            var startedAt = _playbackStartedAt.GetValueOrDefault(streamId);
            var lastEvalAt = _playbackLastEvalAt.GetValueOrDefault(streamId);
            if (!IsEvaluationDue(startedAt, lastEvalAt))
                return Task.CompletedTask;
        }
        _playbackLastEvalAt[streamId] = CpuTimestamp.Now;
        var reason = verdict switch {
            < 0 => PlaybackQualityReason.Backoff,
            > 0 => PlaybackQualityReason.Climb,
            _ => PlaybackQualityReason.Stable,
        };
        return RecomputePlaybackQuality(reason, cancellationToken);
    }

    public Task RequestPlaybackQualityReduction(
        StreamId streamId,
        VideoSourceKind sourceKind,
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
                    sourceKind,
                    new PlaybackHealthSnapshot(
                        IncomingByteRate: 0,
                        BufferSpanMsEma: 0,
                        KeyframeSkipsInWindow: 0,
                        DecoderQueueDepthEma: Constants.Video.HighBufferDepthThreshold + 1,
                        CurrentMaxLayerId: 2,
                        CurrentMaxTemporalLayerId: int.MaxValue,
                        Priority: PlaybackStreamPriority.Primary,
                        StreamAgeMs: int.MaxValue,
                        QualityReductionRequested: true),
                    Verdict: -1,
                    LastSeen: CpuTimestamp.Now,
                    PeakIncomingByteRate: 0,
                    RequestedMaxLayerId: 0,
                    DesiredVideoSize: VideoSize.None);
            }
        }
        return RecomputePlaybackQuality(PlaybackQualityReason.Backoff, cancellationToken);
    }

    public Task OnPlaybackViewportChanged(
        StreamId streamId,
        VideoSourceKind sourceKind,
        double renderCssLongSide,
        double renderDevicePixelRatio,
        PlaybackStreamPriority priority,
        CancellationToken cancellationToken)
    {
        var desiredSize = VideoSizeExt.FromLongSide(renderCssLongSide, renderDevicePixelRatio);
        var currentMaxLayerId = GetBestLayerFor(sourceKind, desiredSize);
        lock (_playbackLock) {
            if (!_playbackStartedAt.ContainsKey(streamId))
                _playbackStartedAt[streamId] = CpuTimestamp.Now;
            var startedAt = _playbackStartedAt.GetValueOrDefault(streamId);
            if (_playbackByStream.TryGetValue(streamId, out var state)) {
                var snapshot = state.Snapshot with {
                    Priority = priority,
                    CurrentMaxLayerId = currentMaxLayerId,
                    RenderCssLongSide = renderCssLongSide,
                    RenderDevicePixelRatio = renderDevicePixelRatio,
                    StreamAgeMs = Math.Max(
                        state.Snapshot.StreamAgeMs,
                        (int)startedAt.Elapsed.TotalMilliseconds),
                };
                var verdict = PlaybackVerdictClassifier.Classify(
                    snapshot.BufferSpanMsEma,
                    PlaybackThresholds.Defaults,
                    snapshot.QualityReductionRequested);
                _playbackByStream[streamId] = state with {
                    SourceKind = sourceKind,
                    Snapshot = snapshot,
                    Verdict = verdict,
                    LastSeen = CpuTimestamp.Now,
                    RequestedMaxLayerId = currentMaxLayerId,
                    DesiredVideoSize = desiredSize,
                };
            }
            else {
                _playbackByStream[streamId] = new PlaybackHealthState(
                    sourceKind,
                    new PlaybackHealthSnapshot(
                        IncomingByteRate: 0,
                        BufferSpanMsEma: 0,
                        KeyframeSkipsInWindow: 0,
                        DecoderQueueDepthEma: 0,
                        CurrentMaxLayerId: currentMaxLayerId,
                        CurrentMaxTemporalLayerId: int.MaxValue,
                        Priority: priority,
                        StreamAgeMs: 0,
                        RenderCssLongSide: renderCssLongSide,
                        RenderDevicePixelRatio: renderDevicePixelRatio),
                    Verdict: 0,
                    LastSeen: CpuTimestamp.Now,
                    PeakIncomingByteRate: 0,
                    RequestedMaxLayerId: currentMaxLayerId,
                    DesiredVideoSize: desiredSize);
            }
            var lastEvalAt = _playbackLastEvalAt.GetValueOrDefault(streamId);
            if (!IsEvaluationDue(startedAt, lastEvalAt, force: true))
                return Task.CompletedTask;

            _playbackLastEvalAt[streamId] = CpuTimestamp.Now;
        }
        return RecomputePlaybackQuality(PlaybackQualityReason.ActiveSetChanged, cancellationToken);
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

    public PlaybackQualitySnapshot PlaybackSnapshot => _playbackSnapshot;

    public RecordingQualitySnapshot GetRecordingSnapshot(VideoSourceKind kind)
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
                // Reconnect = effectively a fresh stream from the QC's
                // perspective: re-arm the per-kind/per-stream cooldown so
                // the first decisions after reconnect aren't a thrash burst.
                _recordingStartedAt.Clear();
                _recordingLastEvalAt.Clear();
                lock (_playbackLock) {
                    _playbackStartedAt.Clear();
                    _playbackLastEvalAt.Clear();
                }
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

            await RecomputePlaybackQuality(PlaybackQualityReason.Stable, cancellationToken).ConfigureAwait(false);
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

    private async Task ApplyRecordingQuality(
        VideoSourceKind kind,
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
        var rawCapacity = _playbackEstimator.Step(aggregateHealth, sumIncomingBytesPerSec);
        // Debug knob: scale the AIMD-estimated capacity before allocation so
        // a developer can simulate a bandwidth-constrained or over-provisioned
        // path without touching the real network.
        var capacity = (long)(rawCapacity * _debugBandwidthMultiplier);
        var verdicts = entries.ToDictionary(x => x.Key.Value, x => x.Value.Verdict);

        var primaries = entries
            .Where(x => x.Value.Snapshot.Priority == PlaybackStreamPriority.Primary)
            .Select(ToStreamRequest)
            .ToArray();
        var secondaries = entries
            .Where(x => x.Value.Snapshot.Priority != PlaybackStreamPriority.Primary)
            .Select(ToStreamRequest)
            .ToArray();
        var maxLayerId = _debugMaxPlaybackLayerCount is { } maxLayerCount
            ? maxLayerCount - 1
            : (int?)null;
        var requested = Allocator.Allocate(capacity, primaries, secondaries, maxLayerId);
        var requestedMap = new ApiMap<string, ReceiveQuality>();
        foreach (var (streamId, _) in entries)
            requestedMap[streamId.Value] = requested.GetValueOrDefault(streamId.Value, ReceiveQuality.Lowest);

        // Signal inputs published for the diagnostics modal: per-stream
        // allocated bitrate (the layer the allocator just picked, computed
        // via layer + codec) and the raw buffer-span EMA reported by the
        // receiver.
        var streamSignals = new Dictionary<string, PlaybackStreamSignals>();
        foreach (var (streamId, state) in entries) {
            var ladder = VideoRecorder.BuildLadder(state.SourceKind);
            var pickedLayer = requestedMap[streamId.Value].MaxLayerId;
            var layer = Math.Clamp(pickedLayer, 0, ladder.Count - 1);
            var allocated = ladder[layer].GetByteRate(state.Snapshot.Codec);
            streamSignals[streamId.Value] = new PlaybackStreamSignals(
                AllocatedBytesPerSec: allocated,
                BufferSpanMsEma: state.Snapshot.BufferSpanMsEma);
        }
        _playbackSnapshot = new PlaybackQualitySnapshot(capacity, aggregateHealth, verdicts, streamSignals);
        Log.LogInformation(
            "PlaybackQuality: reason={Reason} capacity={Capacity} aggHealth={AggHealth:F2} "
            + "streams=[{Streams}]",
            reason, capacity, aggregateHealth,
            string.Join(", ", entries.Select(x =>
                $"{x.Key.Value}:v{x.Value.Verdict}/size={x.Value.DesiredVideoSize}/req=L{requestedMap[x.Key.Value].MaxLayerId}/cur=L{x.Value.Snapshot.CurrentMaxLayerId}"
                + $"/rate={x.Value.Snapshot.IncomingByteRate}/peak={x.Value.PeakIncomingByteRate}"
                + $"/buf={x.Value.Snapshot.BufferSpanMsEma:F0}ms"
                + $"/skips={x.Value.Snapshot.KeyframeSkipsInWindow}"
                + $"/qDepth={x.Value.Snapshot.DecoderQueueDepthEma:F0}"
                + $"/qReduce={x.Value.Snapshot.QualityReductionRequested}"
                + $"/age={x.Value.Snapshot.StreamAgeMs}ms")));
        var streamInfoMap = new ApiMap<string, PlaybackStreamInfo>();
        foreach (var (streamId, state) in entries) {
            var requestedQuality = requestedMap[streamId.Value];
            streamInfoMap[streamId.Value] = new PlaybackStreamInfo(
                state.Snapshot.IncomingByteRate,
                state.Snapshot.BufferSpanMsEma,
                state.Snapshot.KeyframeSkipsInWindow,
                state.Snapshot.DecoderQueueDepthEma,
                requestedQuality.MaxLayerId,
                requestedQuality.MaxTemporalLayerId,
                state.Snapshot.Priority,
                state.Verdict,
                state.Snapshot.LatencyMsEma);
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
                .Select(x => new PlaybackStreamHint(x.Key.Value, x.Value.Snapshot.CurrentMaxLayerId))
                .ToArray(),
            requestedMap,
            cancellationToken).ConfigureAwait(false);

        return;

        static StreamRequest ToStreamRequest(KeyValuePair<StreamId, PlaybackHealthState> entry)
        {
            var maxLayerId = entry.Value.Snapshot.QualityReductionRequested
                ? 0
                : entry.Value.RequestedMaxLayerId;
            var rates = EstimateLayerRates(entry.Value, maxLayerId);
            return new StreamRequest(entry.Key.Value, rates, maxLayerId);
        }
    }

    private static IReadOnlyList<long> EstimateLayerRates(
        PlaybackHealthState state,
        int maxLayerId)
    {
        var ladder = VideoRecorder.BuildLadder(state.SourceKind);
        var targetLayer = Math.Clamp(maxLayerId, 0, ladder.Count - 1);
        var rates = new long[ladder.Count];
        for (var i = 0; i < ladder.Count; i++)
            rates[i] = ladder[i].GetByteRate(state.Snapshot.Codec);

        var targetRate = Math.Max(1, rates[targetLayer]);
        // Peak protects against underestimating rich content after subscribing
        // to a lower layer, but keyframes / startup bursts can inflate it. Bound
        // it to the same 2.5x over-delivery margin used by video QC elsewhere.
        var cappedPeak = Math.Min(
            state.PeakIncomingByteRate,
            (long)(targetRate * Constants.Video.ThroughputOverDeliveryRatio));
        rates[targetLayer] = Math.Max(targetRate, cappedPeak);
        return rates;
    }

    private static long ComputeDecayedPeak(PlaybackHealthState? prev, long currentRate)
    {
        var current = Math.Max(0, currentRate);
        if (prev is null)
            return current;
        var elapsedSec = Math.Max(0, prev.LastSeen.Elapsed.TotalSeconds);
        var decayed = (long)(prev.PeakIncomingByteRate * Math.Pow(PeakDecayPerSecond, elapsedSec));
        return Math.Max(decayed, current);
    }

    private static bool IsEvaluationDue(CpuTimestamp startedAt, CpuTimestamp lastEvalAt, bool force = false)
    {
        // Cooldown is unconditional even with force=true: layer requests during
        // the L2-keyframe wait + EMA(10) ramp-up are based on noisy signals.
        var age = startedAt.Elapsed;
        if (age < QcStartupCooldown)
            return false;
        if (force)
            return true;
        var sinceLast = lastEvalAt.Elapsed;
        var required = age < QcSettlingDuration ? QcSettlingInterval : QcSteadyInterval;
        return sinceLast >= required;
    }

    private static VideoSize GetTopVideoSize(VideoSourceKind sourceKind)
    {
        var ladder = VideoRecorder.BuildLadder(sourceKind);
        return ladder[^1].Size;
    }

    private static VideoSize GetDesiredVideoSize(PlaybackHealthSnapshot snapshot, VideoSourceKind sourceKind)
    {
        var size = snapshot.RenderVideoSize;
        return size == VideoSize.None ? GetTopVideoSize(sourceKind) : size;
    }

    private static int GetBestLayerFor(VideoSourceKind sourceKind, VideoSize desiredSize)
    {
        var ladder = VideoRecorder.BuildLadder(sourceKind);
        if (desiredSize == VideoSize.None)
            return ladder.Count - 1;

        var desiredWidth = desiredSize.LongSide();
        var bestLayer = 0;
        var bestDelta = int.MaxValue;
        for (var i = 0; i < ladder.Count; i++) {
            var delta = Math.Abs(ladder[i].Width - desiredWidth);
            if (delta >= bestDelta)
                continue;
            bestLayer = i;
            bestDelta = delta;
        }
        return bestLayer;
    }

    private static PlaybackHealthSnapshot WithRenderFallback(
        PlaybackHealthSnapshot snapshot,
        PlaybackHealthState? previous)
    {
        if (snapshot.RenderCssLongSide > 0 || snapshot.RenderDevicePixelRatio > 0 || previous is null)
            return snapshot;
        return snapshot with {
            RenderCssLongSide = previous.Snapshot.RenderCssLongSide,
            RenderDevicePixelRatio = previous.Snapshot.RenderDevicePixelRatio,
            Priority = previous.Snapshot.Priority,
        };
    }

    private List<KeyValuePair<StreamId, PlaybackHealthState>> GetFreshPlaybackEntries()
    {
        lock (_playbackLock) {
            var staleStreamIds = _playbackByStream
                .Where(x => x.Value.LastSeen.Elapsed > PlaybackHealthTtl)
                .Select(x => x.Key)
                .ToArray();
            foreach (var streamId in staleStreamIds) {
                _playbackByStream.Remove(streamId);
                _playbackStartedAt.Remove(streamId);
                _playbackLastEvalAt.Remove(streamId);
            }
            return _playbackByStream.ToList();
        }
    }

    private static int? NormalizeLayerCount(int? layerCount)
        => layerCount is >= 1 and <= 3 ? layerCount : null;

    private async Task UpdateRequestedReceiveQualityRegistry(
        IReadOnlyList<PlaybackStreamHint> streamHints,
        ApiMap<string, ReceiveQuality>? requested,
        CancellationToken cancellationToken)
    {
        var jsMethod = $"{BlazorUIAppModule.ImportName}.setRequestedReceiveQuality";
        foreach (var hint in streamHints) {
            if (requested is not null && requested.TryGetValue(hint.StreamId, out var q)) {
                await Hub.JS.InvokeVoidAsync(
                    jsMethod, cancellationToken, hint.StreamId, q.MaxLayerId, q.MaxTemporalLayerId)
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
        IReadOnlyList<PlaybackStreamHint> streamHints,
        CancellationToken cancellationToken)
    {
        var jsMethod = $"{BlazorUIAppModule.ImportName}.setRequestedReceiveQuality";
        foreach (var hint in streamHints) {
            await Hub.JS.InvokeVoidAsync(
                jsMethod, cancellationToken, hint.StreamId, (object?)null, (object?)null)
                .ConfigureAwait(false);
        }
    }

    private static ApiMap<string, ReceiveQuality> BuildLayerCapQuality(
        int? layerCount,
        IReadOnlyList<PlaybackStreamHint> hints)
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
        IReadOnlyDictionary<string, int> Verdicts,
        IReadOnlyDictionary<string, PlaybackStreamSignals> Signals)
    {
        public static readonly PlaybackQualitySnapshot Empty = new(
            EstimatedCapacityBytesPerSec: 0,
            AggregateHealth: 0,
            Verdicts: new Dictionary<string, int>(),
            Signals: new Dictionary<string, PlaybackStreamSignals>());
    }

    public sealed record PlaybackStreamSignals(
        long AllocatedBytesPerSec,
        double BufferSpanMsEma);

    public sealed record RecordingQualitySnapshot(
        VideoSourceKind Kind,
        RecordingQualityState? State,
        RecorderHealthSnapshot? Health,
        int Signal,
        RecordingQualityReason Reason,
        int? DebugMaxLayerCount);

    public sealed record PlaybackStreamHint(string StreamId, int CurrentLayerId);

    public sealed record RecordingThresholds(
        double EncodeRatioBadAbove,
        double EncodeRatioGoodBelow,
        double LastAckBadMs,
        double LastAckGoodMs,
        double SenderFrameDropRatioBadAbove,
        double SenderFrameDropRatioGoodBelow,
        int MinTargetLayerCount,
        int MaxTargetLayerCount,
        int ConsecutiveGoodForClimb,
        int CooldownTicksAfterBackoff)
    {
        public static RecordingThresholds Defaults => new(
            EncodeRatioBadAbove: 1.333, // < 20fps
            EncodeRatioGoodBelow: 0.333, // > 90fps
            LastAckBadMs: Constants.Video.LastAckBadMs,
            LastAckGoodMs: Constants.Video.LastAckGoodMs,
            SenderFrameDropRatioBadAbove: 0.20,
            SenderFrameDropRatioGoodBelow: 0.10, // (30 - 27) / 30 = 0.1: 27 FPS is still OK
            MinTargetLayerCount: 1,
            MaxTargetLayerCount: VideoLayerDef.MaxLayerCount,
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
                h.EncodeRatioEma > t.EncodeRatioBadAbove
                || (h.LastAckAgeMs >= 0 && h.LastAckAgeMs > t.LastAckBadMs)
                || h.SenderFrameDropRatioEma >= t.SenderFrameDropRatioBadAbove;
            if (anyBad)
                return -1;

            var allGood =
                h.EncodeRatioEma < t.EncodeRatioGoodBelow
                && (h.LastAckAgeMs < 0 || h.LastAckAgeMs < t.LastAckGoodMs)
                && h.SenderFrameDropRatioEma < t.SenderFrameDropRatioGoodBelow;
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
        double BufferDurationTooHighMs,
        long MinCapacityBytesPerSec,
        long ColdStartCapacityBytesPerSec,
        double ClimbCap,
        double BackoffFactor)
    {
        public static PlaybackThresholds Defaults => new (
            BufferDurationTooHighMs: Constants.Video.BufferDurationTooHighMs,
            MinCapacityBytesPerSec: 50_000,
            ColdStartCapacityBytesPerSec: 1_500_000,
            ClimbCap: 1.4142135623730951,   // √2
            BackoffFactor: 0.7);
    }

    /// <summary>
    /// Pure per-stream classifier: -1 (bad), 0 (neutral), +1 (good).
    /// Receiver-domain only: reacts to local decoder/main-thread overload via
    /// QualityReductionRequested, and treats a buffer EMA inside the healthy
    /// band as a positive signal. Sender-side starvation indicators (low
    /// buffer, keyframe skips, missing-segment counters) intentionally do
    /// not feed this verdict — those problems are owned by the sender's QC.
    /// </summary>
    public static class PlaybackVerdictClassifier
    {
        public static int Classify(
            double bufferSpanMsEma,
            PlaybackThresholds t,
            bool qualityReductionRequested = false)
        {
            if (qualityReductionRequested)
                return -1;
            if (bufferSpanMsEma > 0 && bufferSpanMsEma <= t.BufferDurationTooHighMs)
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
        IReadOnlyList<long> PredictedRatesByLayer,
        int? MaxLayerId = null)
    {
        public long PredictedRateAtBase => PredictedRatesByLayer.Count == 0 ? long.MaxValue : PredictedRatesByLayer[0];
    }

    /// <summary>
    /// Greedy budget allocator: streams are visited by importance order
    /// (primaries first, then secondaries). Each stream gets the closest layer
    /// at or below its desired cap that fits the remaining budget.
    /// Streams that don't fit at the base layer are dropped from the result —
    /// the caller maps that to <see cref="ReceiveQuality.Lowest"/>.
    /// </summary>
    public static class Allocator
    {
        public static IReadOnlyDictionary<string, ReceiveQuality> Allocate(
            long budgetBytesPerSec,
            IReadOnlyList<StreamRequest> primaries,
            IReadOnlyList<StreamRequest> secondaries,
            int? maxLayerId = null)
        {
            var result = new Dictionary<string, ReceiveQuality>();
            var remaining = budgetBytesPerSec;
            Allocate(primaries);
            Allocate(secondaries);
            return result;

            void Allocate(IReadOnlyList<StreamRequest> requests)
            {
                foreach (var req in requests) {
                    var layer = FindBestLayer(req, remaining, maxLayerId);
                    if (layer < 0)
                        continue;
                    result[req.StreamId] = new ReceiveQuality(layer, int.MaxValue);
                    remaining -= req.PredictedRatesByLayer[layer];
                }
            }

            static int FindBestLayer(StreamRequest req, long remaining, int? globalMaxLayerId)
            {
                if (req.PredictedRatesByLayer.Count == 0)
                    return -1;
                var effectiveMaxLayerId = MinLayer(globalMaxLayerId, req.MaxLayerId)
                    ?? req.PredictedRatesByLayer.Count - 1;
                effectiveMaxLayerId = Math.Clamp(effectiveMaxLayerId, 0, req.PredictedRatesByLayer.Count - 1);
                for (var layer = effectiveMaxLayerId; layer >= 0; layer--) {
                    if (remaining >= req.PredictedRatesByLayer[layer])
                        return layer;
                }
                return -1;
            }

            static int? MinLayer(int? globalMax, int? streamMax)
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
        double BufferSpanMsEma,
        int KeyframeSkipsInWindow,
        double DecoderQueueDepthEma,
        int CurrentMaxLayerId,
        int CurrentMaxTemporalLayerId,
        PlaybackStreamPriority Priority,
        int StreamAgeMs,
        bool QualityReductionRequested = false,
        double LatencyMsEma = 0,
        double RenderCssLongSide = 0,
        double RenderDevicePixelRatio = 0,
        string Codec = "")
    {
        public VideoSize RenderVideoSize
            => VideoSizeExt.FromLongSide(RenderCssLongSide, RenderDevicePixelRatio);
    }

    private sealed record PlaybackHealthState(
        VideoSourceKind SourceKind,
        PlaybackHealthSnapshot Snapshot,
        int Verdict,
        CpuTimestamp LastSeen,
        long PeakIncomingByteRate,
        // Per-stream max layer requested for this allocation cycle.
        int RequestedMaxLayerId,
        VideoSize DesiredVideoSize);
}
