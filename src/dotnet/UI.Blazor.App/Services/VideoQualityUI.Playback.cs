using ActualChat.Bandwidth;
using ActualChat.Streaming;
using ActualChat.UI.Blazor.App.Module;
using ActualChat.Video;

namespace ActualChat.UI.Blazor.App.Services;

public sealed partial class VideoQualityUI
{
    private static readonly TimeSpan PlaybackHealthTtl = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan PlaybackQualityKeepAlivePeriod = TimeSpan.FromMinutes(1);

    // Per-stream observed-rate decay. The allocator mostly trusts static ladder
    // rates, but keeps a short-lived guard for real encoder over-delivery on the
    // requested tier. Input is already a 3s JS-side window, so this should decay
    // quickly; otherwise a startup/keyframe burst can pin L2 as "too expensive".
    private const double ObservedRateDecayPerSecond = 0.80; // halves in ~3s
    private const double ObservedRateCapMultiplier = 1.50;
    // Sustained-calm ticks before the receiver re-probes a higher layer after
    // HasSeenBadSignal latched. Recovers faster than the estimator's 20-tick
    // re-anchor; the exponential probe cooldown still gates repeated attempts.
    private const int ReprobeCalmStreak = 3;

    // A decoder stuck waiting for a keyframe (dropping every delta while
    // pendingError is set) reads as Bad with a high decode deficit. Periodic
    // keyframes are ~3 s apart and there is no other receiver→sender keyframe
    // path, so without a PLI the stall persists for up to a full keyframe
    // interval and the hang watchdog re-fires into a ~3 s loop. Requesting a
    // keyframe when the deficit is this high bounds the stall to the server PLI
    // cooldown (~1 s). Threshold sits well above any healthy-stream deficit
    // (a settled stream is near 0; the stuck one observed at ~0.96).
    private const double KeyFrameRequestDeficitThreshold = 0.5;
    private static readonly TimeSpan KeyFrameRequestCooldown = TimeSpan.FromSeconds(1);
    private readonly Dictionary<StreamId, CpuTimestamp> _lastKeyFrameRequestAt = new();

    // Stall notes ride the next quality push so a stall reaches the server logs:
    // the receiver's own console isn't collectable from users in the field.
    // Same per-stream cooldown shape as _lastKeyFrameRequestAt — a stalling
    // stream can re-trigger every tick otherwise.
    private static readonly TimeSpan StallReportCooldown = TimeSpan.FromSeconds(15);
    private readonly Dictionary<StreamId, CpuTimestamp> _lastStallReportAt = new();
    private readonly Dictionary<StreamId, string> _pendingStallNotes = new();

    // Stream-age-aware throttle on QC decisions: cooldown for the first
    // StartupCooldown, then evaluate at SettlingInterval until SettlingDuration
    // of stream age, then SteadyInterval. Prevents premature thrash and keeps
    // steady-state ChangeQuality traffic low.
    private readonly Dictionary<StreamId, CpuTimestamp> _playbackStartedAt = new();
    private readonly Dictionary<StreamId, CpuTimestamp> _playbackLastEvalAt = new();
    private readonly Dictionary<StreamId, PlaybackStatsState> _playbackByStream = new();
    private readonly Lock _playbackLock = new();
    // Snapshot of the latest panel mode observed by WatchVideoPanelModeEdges.
    // Used by GetFreshPlaybackEntries to skip staleness pruning while QC has
    // intentionally paused inbound frames (Hidden/Collapsed) — without that,
    // OnPlaybackStats dries up, entries age out, and resume can't dispatch.
    private VideoPanelMode _currentPanelMode = VideoPanelMode.Inline;
    // Snapshot of BackgroundStateTracker.IsBackground observed by WatchBackgroundState.
    // Hidden tab / backgrounded app pauses every stream just like VideoPanelMode.Hidden.
    private volatile bool _isBackground;
    private readonly BandwidthEstimator _inboundBwEstimator;
    private PlaybackQualitySnapshot _playbackSnapshot = PlaybackQualitySnapshot.Empty;
    // Per-stream classifier instances — each owns its own streak counters for
    // Downlink + Decoder. Lazy-created on first OnPlaybackStats per stream.
    private readonly Dictionary<StreamId, ReceiverHealthClassifier> _receiverHealthByStream = new();
    private readonly Dictionary<StreamId, DownlinkHealth> _lastDownlinkHealthByStream = new();
    private readonly Dictionary<StreamId, DecoderHealth> _lastDecoderHealthByStream = new();
    // Demand actually asked for, surfaced in the panel. Without it there's no way
    // to tell a correct low layer (small tile) from a demand bug — the dump shows
    // the received tier but never the size that asked for it.
    private readonly Dictionary<StreamId, InboundDemand> _lastInboundDemandByStream = new();
    // Previous tick's demand, to spot a demand increase — prior probe failures at
    // a lower requested rate don't predict the newly-requested one.
    private readonly Dictionary<StreamId, int> _prevDemandLayerCountByStream = new();
    // Sticky decoder cap with edge-triggered demote. Set on Good→Bad
    // transition, cleared on =Good. Marginal holds the last value.
    private readonly DecoderCapState _decoderCapState = new();
    // Snapshot of the previous tick's requested layer per stream, used to
    // detect allocator-driven cap moves for the decision log.
    private readonly Dictionary<string, int> _prevRequestedLayerByStream = new();
    private readonly SemaphoreSlim _recomputeGate = new(1, 1);
    private HealthVerdict _lastAggregateDownlinkVerdict = HealthVerdict.Unknown;
    // Set by GetAllocationCapacity each tick; surfaced in the receiver decision log.
    private string _lastInboundProbeNote = "";

    public BandwidthEstimator InboundBandwidthEstimator => _inboundBwEstimator;
    public PlaybackQualitySnapshot PlaybackSnapshot => _playbackSnapshot;
    public HealthVerdict AggregateDownlinkVerdict => _lastAggregateDownlinkVerdict;
    public IReadOnlyDictionary<StreamId, DownlinkHealth> InboundDownlinkHealthByStream
        => _lastDownlinkHealthByStream;
    public IReadOnlyDictionary<StreamId, DecoderHealth> InboundDecoderHealthByStream
        => _lastDecoderHealthByStream;
    public IReadOnlyDictionary<StreamId, InboundDemand> InboundDemandByStream
        => _lastInboundDemandByStream;
    public int InboundDecoderCapStreamCount => _decoderCapState.Caps.Count;

    public Task OnPlaybackStats(
        StreamId streamId,
        VideoSourceKind sourceKind,
        PlaybackStats snapshot,
        bool hasDimensions,
        CancellationToken cancellationToken)
    {
        _whenActuallyUsed.TrySetResult();
        var verdict = PlaybackVerdictClassifier.Classify(
            snapshot.BufferSpanMsEma,
            PlaybackThresholds.Defaults);
        bool isFirstTick;
        lock (_playbackLock) {
            var prev = _playbackByStream.GetValueOrDefault(streamId);
            snapshot = WithRenderFallback(snapshot, prev);
            var observedPeak = ComputeDecayedObservedRate(prev, snapshot.IncomingByteRate);
            var desiredSize = GetDesiredVideoSize(snapshot, sourceKind, hasDimensions);
            var requestedLayerCount = GetBestLayerFor(sourceKind, desiredSize) + 1;
            _playbackByStream[streamId] = new PlaybackStatsState(
                sourceKind, snapshot, verdict, CpuTimestamp.Now, observedPeak,
                requestedLayerCount, desiredSize);
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

    public Task OnPlaybackStalled(
        StreamId streamId,
        string reason,
        CancellationToken cancellationToken)
    {
        lock (_playbackLock) {
            if (!TryStartStallReportLocked(streamId))
                return Task.CompletedTask;

            _pendingStallNotes[streamId] = reason;
        }
        return RecomputePlaybackQuality(PlaybackQualityReason.Stalled, cancellationToken);
    }

    public Task OnPlaybackViewportChanged(
        StreamId streamId,
        VideoSourceKind sourceKind,
        double renderCssLongSide,
        double renderDevicePixelRatio,
        PlaybackStreamPriority priority,
        bool hasDimensions,
        CancellationToken cancellationToken)
    {
        // Pre-layout (!hasDimensions): focused tile → top, others stay tiny.
        var desiredSize = hasDimensions || priority != PlaybackStreamPriority.Primary
            ? VideoSizeExt.FromLongSide(renderCssLongSide, renderDevicePixelRatio)
            : VideoSize.None;
        var currentLayerCount = GetBestLayerFor(sourceKind, desiredSize) + 1;
        lock (_playbackLock) {
            if (!_playbackStartedAt.ContainsKey(streamId))
                _playbackStartedAt[streamId] = CpuTimestamp.Now;
            var startedAt = _playbackStartedAt.GetValueOrDefault(streamId);
            if (_playbackByStream.TryGetValue(streamId, out var state)) {
                var snapshot = state.Snapshot with {
                    Priority = priority,
                    RenderCssLongSide = renderCssLongSide,
                    RenderDevicePixelRatio = renderDevicePixelRatio,
                    StreamDurationMs = Math.Max(
                        state.Snapshot.StreamDurationMs,
                        (int)startedAt.Elapsed.TotalMilliseconds),
                };
                var verdict = PlaybackVerdictClassifier.Classify(
                    snapshot.BufferSpanMsEma,
                    PlaybackThresholds.Defaults);
                _playbackByStream[streamId] = state with {
                    SourceKind = sourceKind,
                    Snapshot = snapshot,
                    Verdict = verdict,
                    LastSeen = CpuTimestamp.Now,
                    RequestedLayerCount = currentLayerCount,
                    DesiredVideoSize = desiredSize,
                };
            }
            else {
                _playbackByStream[streamId] = new PlaybackStatsState(
                    sourceKind,
                    PlaybackStats.Empty with {
                        Priority = priority,
                        RenderCssLongSide = renderCssLongSide,
                        RenderDevicePixelRatio = renderDevicePixelRatio,
                    },
                    Verdict: 0,
                    LastSeen: CpuTimestamp.Now,
                    ObservedPeakByteRate: 0,
                    RequestedLayerCount: currentLayerCount,
                    DesiredVideoSize: desiredSize);
            }
            var lastEvalAt = _playbackLastEvalAt.GetValueOrDefault(streamId);
            if (!IsEvaluationDue(startedAt, lastEvalAt, force: true))
                return Task.CompletedTask;

            _playbackLastEvalAt[streamId] = CpuTimestamp.Now;
        }
        return RecomputePlaybackQuality(PlaybackQualityReason.ActiveSetChanged, cancellationToken);
    }

    // Private methods

    // Backstop for a decoder stuck waiting for a keyframe: the receive-filter
    // only switches a layer (and a recovering decoder only re-configures) on a
    // keyframe, but periodic keyframes are ~3 s apart and a sender prune can
    // remove the layer the receiver was holding. Ask the sender for a fresh
    // keyframe; the server PLI path is itself cooldown-collapsed across viewers,
    // and the per-stream cooldown here keeps a single viewer from spamming.
    private void MaybeRequestKeyFrame(
        StreamId streamId,
        DecoderHealth decoder,
        CancellationToken cancellationToken)
    {
        if (decoder.Verdict != HealthVerdict.Bad
            || decoder.DecodeDeficitEma < KeyFrameRequestDeficitThreshold)
            return;
        var now = CpuTimestamp.Now;
        if (_lastKeyFrameRequestAt.TryGetValue(streamId, out var last)
            && last.Elapsed < KeyFrameRequestCooldown)
            return;
        _lastKeyFrameRequestAt[streamId] = now;
        _ = LiveVideoStreams
            .RequestKeyFrame(Session, streamId.Value, cancellationToken)
            .SuppressExceptions();
    }

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

    // Serialized: triggers race in from independent watchers (panel mode,
    // background, thermal, connectivity, keep-alive, stats callbacks) and the
    // recompute mutates plain-dictionary state (_prevRequestedLayerByStream,
    // per-stream health maps) across awaits.
    private async Task RecomputePlaybackQuality(PlaybackQualityReason reason, CancellationToken cancellationToken)
    {
        await _recomputeGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try {
            await RecomputePlaybackQualityLocked(reason, cancellationToken).ConfigureAwait(false);
        }
        finally {
            _recomputeGate.Release();
        }
    }

    private async Task RecomputePlaybackQualityLocked(PlaybackQualityReason reason, CancellationToken cancellationToken)
    {
        var entries = GetFreshPlaybackEntries();
        if (entries.Count == 0) {
            _playbackSnapshot = PlaybackQualitySnapshot.Empty;
            return;
        }

        var sumIncomingBytesPerSec = entries.Sum(x => Math.Max(0, x.Value.Snapshot.IncomingByteRate));
        var playbackRateEma = ComputeByteWeightedPlaybackRate(entries);
        var receiverDropRatio = ComputeAggregateReceiveDropRatio(entries);

        // Per-stream Downlink + Decoder classification. Drift defaults to
        // Downlink (per plan §"Attribution"): when DecodeRatioEma is healthy
        // we attribute drift to Downlink anyway, so the existing playbackRate
        // signal stays as additional Downlink evidence.
        var aggregateDownlinkVerdict = HealthVerdict.Unknown;
        var aggregateDecoderVerdict = HealthVerdict.Unknown;
        foreach (var (streamId, state) in entries) {
            if (!_receiverHealthByStream.TryGetValue(streamId, out var classifier)) {
                classifier = new ReceiverHealthClassifier();
                _receiverHealthByStream[streamId] = classifier;
            }
            var snap = state.Snapshot;
            var streamDownlink = classifier.ClassifyDownlink(
                serverToReceiverLatencyEma: snap.DownlinkLatencyEma,
                arrivalIntervalEma: snap.ArrivalIntervalEma,
                serverPathDropRatio: receiverDropRatio, // TODO: split dropTrace stages 31-34+61-62 from 63-64.
                bufferUnderrunRatio: snap.BufferUnderrunRatio,
                incomingByteRateDeficit: 1.0); // TODO: actual / expected-for-layer once allocator publishes it.
            var streamDecoder = classifier.ClassifyDecoder(
                decodeRatioEma: snap.DecodeRatioEma,
                decodeDeficitEma: snap.DecodeDeficitEma,
                hangRateIn60s: snap.HangRateIn60s,
                recoveryStreak: snap.RecoveryStreak,
                presentSkipRatio: snap.PresentSkipRatio,
                receiverDecodePathDropRatio: 0); // TODO: split stages 63-64 once dropTrace is split.
            _lastDownlinkHealthByStream[streamId] = streamDownlink;
            _lastDecoderHealthByStream[streamId] = streamDecoder;
            MaybeRequestKeyFrame(streamId, streamDecoder, cancellationToken);
            // OpenTelemetry emission deferred — AppMeters lives in Core.Server
            // (unreferenced from UI.Blazor.App). Wire DTOs will carry the
            // per-leg fields in a follow-up so server-side handlers can record.
            if (streamDownlink.Verdict != HealthVerdict.Unknown
                && (int)streamDownlink.Verdict > (int)aggregateDownlinkVerdict)
                aggregateDownlinkVerdict = streamDownlink.Verdict;
            if (streamDecoder.Verdict != HealthVerdict.Unknown
                && (int)streamDecoder.Verdict > (int)aggregateDecoderVerdict)
                aggregateDecoderVerdict = streamDecoder.Verdict;
            _decoderCapState.OnVerdict(
                streamId.Value, streamDecoder.Verdict, state.RequestedLayerCount);
        }
        // Drop stale entries so per-stream classifiers don't leak.
        var liveStreamIds = entries.Select(x => x.Key).ToHashSet();
        var liveStreamIdStrings = liveStreamIds.Select(x => x.Value).ToHashSet();
        foreach (var sid in _receiverHealthByStream.Keys.Where(k => !liveStreamIds.Contains(k)).ToArray()) {
            _receiverHealthByStream.Remove(sid);
            _lastDownlinkHealthByStream.Remove(sid);
            _lastDecoderHealthByStream.Remove(sid);
            _lastKeyFrameRequestAt.Remove(sid);
            _lastStallReportAt.Remove(sid);
            _prevDemandLayerCountByStream.Remove(sid);
        }
        _decoderCapState.PruneStaleStreams(liveStreamIdStrings);

        _lastAggregateDownlinkVerdict = aggregateDownlinkVerdict;
        var downlinkSignal = VerdictToSignal(aggregateDownlinkVerdict);
        var connection = ConnectivityUI.ConnectionInfo.Value;
        _inboundBwEstimator.Tick(connection, SystemClock.Now, sumIncomingBytesPerSec, downlinkSignal);
        _thermalCap.Tick(SystemClock.Now, ThermalTracker.Level.Value);
        var estimatedCapacity = (long)(_inboundBwEstimator.CeilingBps
            * _debugBandwidthMultiplier
            * _thermalCap.InboundBudgetMultiplier);
        var aggregateHealth = 2 * downlinkSignal - 1;
        var verdicts = entries.ToDictionary(x => x.Key.Value, x => x.Value.Verdict);

        var primaries = entries
            .Where(x => x.Value.Snapshot.Priority == PlaybackStreamPriority.Primary)
            .Select(ToAllocationRequest)
            .ToArray();
        var secondaries = entries
            .Where(x => x.Value.Snapshot.Priority != PlaybackStreamPriority.Primary)
            .Select(ToAllocationRequest)
            .ToArray();
        var healthyStreamIds = entries
            .Where(x => _lastDownlinkHealthByStream.TryGetValue(x.Key, out var dl)
                && dl.Verdict == HealthVerdict.Good
                && (!_lastDecoderHealthByStream.TryGetValue(x.Key, out var dec)
                    || dec.Verdict != HealthVerdict.Bad))
            .Select(x => x.Key.Value)
            .ToHashSet();
        // A demand increase is new information the back-off can't speak to: the
        // cooldown was earned by probes that asked for less. Clear it so a tile
        // the user just enlarged isn't held at a stale ceiling for up to
        // MaxProbeCooldownSec. The calm-streak gate still applies — that one
        // tracks current health, which a demand change says nothing about.
        var demandGrew = false;
        foreach (var (streamId, state) in entries) {
            if (state.RequestedLayerCount > _prevDemandLayerCountByStream.GetValueOrDefault(streamId))
                demandGrew = true;
            _prevDemandLayerCountByStream[streamId] = state.RequestedLayerCount;
        }
        if (demandGrew)
            _inboundBwEstimator.ResetProbeBackoff();

        var capacity = GetAllocationCapacity(
            estimatedCapacity, primaries, secondaries, healthyStreamIds,
            aggregateDownlinkVerdict, aggregateDecoderVerdict, SystemClock.Now);
        var decoderLayerCapDict = _decoderCapState.Caps.Count == 0
            ? null
            : _decoderCapState.Caps;
        var requested = VideoQualityAllocator.Allocate(capacity, primaries, secondaries, decoderLayerCapDict);
        var requestedMap = new ApiMap<string, ReceiveQuality>();
        foreach (var (streamId, state) in entries) {
            var quality = requested.GetValueOrDefault(streamId.Value, ReceiveQuality.Lowest);
            // Display role for the sender's fps shed. Size uses the PURE render
            // demand (RequestedLayerCount, pre-clamp) — a bandwidth/thermal-clamped
            // large view must never read as a thumbnail.
            var isThumbnail = state.Snapshot.Priority != PlaybackStreamPriority.Primary
                && state.RequestedLayerCount <= 1;
            requestedMap[streamId.Value] = quality with { IsThumbnail = isThumbnail };
        }
        // Float (Collapsed) shows only the primary tile — pause every secondary
        // stream. Hide (or a hidden tab / backgrounded app) pauses every stream.
        // The server filter drops every frame while Paused is in effect, so this
        // also throttles inbound bandwidth.
        var panelMode = await Hub.ChatVideoUI.GetVideoPanelMode(cancellationToken).ConfigureAwait(false);
        if (panelMode == VideoPanelMode.Hidden || _isBackground) {
            foreach (var (streamId, _) in entries)
                requestedMap[streamId.Value] = ReceiveQuality.Paused;
        }
        else if (panelMode == VideoPanelMode.Collapsed) {
            foreach (var (streamId, state) in entries) {
                if (state.Snapshot.Priority != PlaybackStreamPriority.Primary)
                    requestedMap[streamId.Value] = ReceiveQuality.Paused;
            }
        }

        var streamSignals = new Dictionary<string, PlaybackStreamSignals>();
        foreach (var (streamId, state) in entries) {
            var ladder = VideoRecorder.BuildLadder(state.SourceKind);
            var pickedLayer = requestedMap[streamId.Value].LayerId;
            var layer = Math.Clamp(pickedLayer, 0, ladder.Count - 1);
            var allocated = ladder[layer].GetByteRate(state.Snapshot.Codec);
            streamSignals[streamId.Value] = new PlaybackStreamSignals(
                AllocatedBytesPerSec: allocated,
                BufferSpanMsEma: state.Snapshot.BufferSpanMsEma);
        }
        _playbackSnapshot = new PlaybackQualitySnapshot(
            capacity, aggregateHealth, verdicts, streamSignals,
            PlaybackRateEma: playbackRateEma,
            DropRatio: receiverDropRatio);
        Log.LogDebug(
            "PlaybackQuality: reason={Reason} ceiling={Ceiling} downlinkVerdict={DownlinkVerdict} " +
            "downlinkSignal={DownlinkSignal:F2} decoderCaps={DecoderCapCount} streams=[{Streams}]",
            reason, capacity, aggregateDownlinkVerdict, downlinkSignal,
            _decoderCapState.Caps.Count,
            string.Join(", ", entries.Select(x =>
                $"{x.Key.Value}:size={x.Value.DesiredVideoSize}/req=L{requestedMap[x.Key.Value].LayerId}"
                + $"/duration={x.Value.Snapshot.StreamDurationMs}ms"
                + $"/buf={x.Value.Snapshot.BufferSpanMsEma:F0}ms"
                + $"/rate={x.Value.Snapshot.IncomingByteRate}/peak={x.Value.ObservedPeakByteRate}"
                + $"/playbackRate={x.Value.Snapshot.PlaybackRateEma:F2}"
                )));
        var streamInfoMap = new ApiMap<string, PlaybackStreamInfo>();
        foreach (var (streamId, state) in entries) {
            streamInfoMap[streamId.Value] = new PlaybackStreamInfo(
                state.Snapshot.IncomingByteRate,
                state.Snapshot.BufferSpanMsEma,
                state.Snapshot.Priority,
                state.Verdict);
        }
        var stallNote = TakePendingStallNotes();
        var info = new PlaybackQualityInfo(
            estimatedCapacity,
            aggregateHealth,
            stallNote.IsNullOrEmpty() ? reason : PlaybackQualityReason.Stalled,
            IsColdStart: false,
            streamInfoMap,
            stallNote);
        _ = LiveVideoStreams.ChangePlaybackQuality(
            Session,
            requestedMap,
            info,
            cancellationToken).SuppressExceptions();
        await UpdateRequestedReceiveQualityRegistry(
            entries
                .Select(x => new PlaybackStreamHint(x.Key.Value, Math.Max(0, x.Value.RequestedLayerCount - 1)))
                .ToArray(),
            requestedMap,
            cancellationToken).ConfigureAwait(false);

        _lastInboundDemandByStream.Clear();
        foreach (var (streamId, state) in entries) {
            var layerId = requestedMap.GetValueOrDefault(streamId.Value, ReceiveQuality.Lowest).LayerId;
            _lastInboundDemandByStream[streamId] = new InboundDemand(
                state.DesiredVideoSize,
                state.RequestedLayerCount,
                state.Snapshot.RenderCssLongSide,
                state.Snapshot.RenderDevicePixelRatio,
                GetLayerCapTag(streamId.Value, layerId, entries));
        }

        // Cap-change detection: compare requested layer + decoder cap maps
        // with the previous tick's snapshot. Pick the first changed stream
        // in lexical order (predictable, deterministic for the log).
        var capChange = "";
        foreach (var (sid, q) in requestedMap.OrderBy(x => x.Key)) {
            var prevLayer = _prevRequestedLayerByStream.GetValueOrDefault(sid, -1);
            // Skip Paused transitions in either direction — they're panel-mode
            // events, not QC decisions, and would produce nonsensical L→-1 rows.
            if (prevLayer < 0 || q.LayerId < 0) continue;
            if (prevLayer != q.LayerId) {
                capChange = $"{ShortStreamId(sid)} L{prevLayer}→L{q.LayerId} ({GetLayerCapTag(sid, q.LayerId, entries)})";
                break;
            }
        }
        // Refresh snapshot maps for the next tick.
        _prevRequestedLayerByStream.Clear();
        foreach (var (sid, q) in requestedMap)
            _prevRequestedLayerByStream[sid] = q.LayerId;

        // Pick the worst stream for the decoder raw-values line so the
        // operator sees the actual numbers behind the aggregate verdict.
        DecoderHealth? worstDecoder = null;
        foreach (var (_, h) in _lastDecoderHealthByStream) {
            if (h.Verdict == HealthVerdict.Unknown) continue;
            if (worstDecoder is null || (int)h.Verdict > (int)worstDecoder.Verdict)
                worstDecoder = h;
        }
        // Pick the worst stream for the downlink raw-values line similarly.
        DownlinkHealth? worstDownlink = null;
        foreach (var (_, h) in _lastDownlinkHealthByStream) {
            if (h.Verdict == HealthVerdict.Unknown) continue;
            if (worstDownlink is null || (int)h.Verdict > (int)worstDownlink.Verdict)
                worstDownlink = h;
        }
        var ceilingKbps = _inboundBwEstimator.CeilingBps * 8 / 1000;
        var currentKbps = _inboundBwEstimator.LastCurrentBps * 8 / 1000;
        var dlReason = aggregateDownlinkVerdict == HealthVerdict.Bad && worstDownlink is not null
            ? $"downlink lat={worstDownlink.ServerToReceiverLatencyEma:F0}ms drop={worstDownlink.ServerPathDropRatio:F2}"
            : "";
        var decReason = aggregateDecoderVerdict == HealthVerdict.Bad && worstDecoder is not null
            ? $"decode deficit={worstDecoder.DecodeDeficitEma * 100:F1}% hang={worstDecoder.HangRateIn60s}"
            : "";
        var inboundReason = !string.IsNullOrEmpty(dlReason) ? dlReason
            : !string.IsNullOrEmpty(decReason) ? decReason
            : !string.IsNullOrEmpty(_lastInboundProbeNote) ? _lastInboundProbeNote
            : _inboundBwEstimator.LastVerdict switch {
                BandwidthVerdict.Good => $"BW ↑ {ceilingKbps} kbps",
                BandwidthVerdict.Bad => $"BW ↓ {ceilingKbps} kbps",
                _ => "stable",
            };
        var rawA = worstDownlink is not null
            ? $"lat={worstDownlink.ServerToReceiverLatencyEma:F0}ms drop={worstDownlink.ServerPathDropRatio:F2} und={worstDownlink.BufferUnderrunRatio:F2} pr={playbackRateEma:F2}"
            : $"pr={playbackRateEma:F2} drop={receiverDropRatio:F2}";
        var rawB = worstDecoder is not null
            ? $"deficit={worstDecoder.DecodeDeficitEma * 100:F1}% ratio={worstDecoder.DecodeRatioEma:F2} hang={worstDecoder.HangRateIn60s} rec={worstDecoder.RecoveryStreak} skip={worstDecoder.PresentSkipRatio:F2}"
            : "";
        var rawBw = $"{(_inboundBwEstimator.LastVerdict == BandwidthVerdict.Good ? "↑" : _inboundBwEstimator.LastVerdict == BandwidthVerdict.Bad ? "↓" : "=")}{ceilingKbps}/cur {currentKbps} kbps";
        AppendInboundDecision(new QualityDecisionEntry(
            SystemClock.Now,
            aggregateDownlinkVerdict,
            aggregateDecoderVerdict,
            _inboundBwEstimator.LastVerdict,
            capChange,
            inboundReason,
            rawA,
            rawB,
            rawBw));

        return;

        StreamAllocationRequest ToAllocationRequest(KeyValuePair<StreamId, PlaybackStatsState> entry)
        {
            var debugCap = _debugMaxPlaybackLayerCount;
            var layerCountCap = Math.Min(
                Math.Min(entry.Value.RequestedLayerCount, _thermalCap.MaxPlaybackLayerCount),
                debugCap ?? int.MaxValue);
            var rates = EstimateLayerRates(entry.Value, layerCountCap);
            var area = Math.Max(1,
                entry.Value.Snapshot.RenderCssLongSide
                * entry.Value.Snapshot.RenderCssLongSide
                * Math.Max(1, entry.Value.Snapshot.RenderDevicePixelRatio)
                * Math.Max(1, entry.Value.Snapshot.RenderDevicePixelRatio));
            return new StreamAllocationRequest(
                entry.Key.Value,
                rates,
                layerCountCap,
                area);
        }
    }

    private static double ComputeByteWeightedPlaybackRate(IReadOnlyList<KeyValuePair<StreamId, PlaybackStatsState>> entries)
    {
        double totalWeight = 0;
        double weightedSum = 0;
        foreach (var (_, state) in entries) {
            var w = Math.Max(1, state.Snapshot.IncomingByteRate);
            totalWeight += w;
            weightedSum += w * Math.Clamp(state.Snapshot.PlaybackRateEma, 0, 1);
        }
        return totalWeight > 0 ? weightedSum / totalWeight : 1;
    }

    // Downlink congestion = frames the server sent but the receiver never got:
    // the ReceiverPull gap detector (stage 61), placed before the encoded buffer
    // on the raw arrival sequence. The other receiver stages are benign and
    // burst at join — ReceiverEncodedBuffer (62) keyframe-gating + skip-to-live,
    // ReceiverDecode (63) decoder warmup, ReceiverPresent (64) present-pacer
    // catch-up, ReceiverSkipToLive (65). Counting those as "drops" spiked the
    // ratio to ~0.5 on localhost at join and falsely flipped downlink to Bad
    // (no streak on the drop leg), ratcheting the bandwidth ceiling down. Real
    // slow-arrival congestion surfaces on the separate buffer-underrun leg.
    private static double ComputeAggregateReceiveDropRatio(IReadOnlyList<KeyValuePair<StreamId, PlaybackStatsState>> entries)
    {
        long totalDrops = 0;
        long totalPresented = 0;
        foreach (var (_, state) in entries) {
            totalDrops += state.Snapshot.DropTrace.GetValueOrDefault(FrameDropStage.ReceiverPull);
            totalPresented += state.Snapshot.PresentedCount;
        }
        var denom = Math.Max(1, totalDrops + totalPresented);
        return (double)totalDrops / denom;
    }

    private static IReadOnlyList<long> EstimateLayerRates(
        PlaybackStatsState state,
        int layerCount)
    {
        var ladder = VideoRecorder.BuildLadder(state.SourceKind);
        var targetLayer = Math.Clamp(layerCount - 1, 0, ladder.Count - 1);
        var rates = new long[ladder.Count];
        for (var i = 0; i < ladder.Count; i++)
            rates[i] = ladder[i].GetByteRate(state.Snapshot.Codec);

        var targetRate = Math.Max(1, rates[targetLayer]);
        // Windowed observed rate protects against real over-delivery, but only
        // within a small cap so transient bursts don't block L2 for a long time.
        var cappedPeak = Math.Min(
            state.ObservedPeakByteRate,
            (long)(targetRate * ObservedRateCapMultiplier));
        rates[targetLayer] = Math.Max(targetRate, cappedPeak);
        return rates;
    }

    private long GetAllocationCapacity(
        long estimatedCapacity,
        IReadOnlyList<StreamAllocationRequest> primaries,
        IReadOnlyList<StreamAllocationRequest> secondaries,
        IReadOnlySet<string> healthyStreamIds,
        HealthVerdict downlinkVerdict,
        HealthVerdict decoderVerdict,
        Moment now)
    {
        // The receiver feeds supply-limited goodput as "current bandwidth", so a
        // ceiling once ratcheted to L0 can never measure enough to climb back —
        // the only way to learn real downlink capacity is to request more and
        // watch it arrive. The startup probe does this before the first bad
        // signal; the re-probe does it again after sustained calm + cooldown,
        // and a failed probe demotes via the normal path (growing the cooldown).
        var startupProbe = !_inboundBwEstimator.HasSeenBadSignal;
        var reprobe = !startupProbe && ShouldReprobe(
            downlinkVerdict,
            decoderVerdict,
            _inboundBwEstimator.CalmTicks,
            ReprobeCalmStreak,
            _inboundBwEstimator.LastCeilingDownAt,
            _inboundBwEstimator.ProbeFailures,
            now,
            _inboundBwEstimator.Config);

        if (!startupProbe && !reprobe) {
            _lastInboundProbeNote = ReprobeCooldownNote(now);
            return estimatedCapacity;
        }

        long probeCapacity = 0;
        if (reprobe) {
            // Every healthy stream may climb to its requested cap; unhealthy
            // streams (or a stressed shared downlink already excluded the probe)
            // stay at floor.
            foreach (var p in primaries)
                probeCapacity += healthyStreamIds.Contains(p.StreamId) ? MaxRateOf(p) : FloorRateOf(p);
            foreach (var s in secondaries)
                probeCapacity += healthyStreamIds.Contains(s.StreamId) ? MaxRateOf(s) : FloorRateOf(s);
        }
        else if (primaries.Count != 0) {
            foreach (var p in primaries)
                probeCapacity += MaxRateOf(p);
            foreach (var s in secondaries)
                probeCapacity += FloorRateOf(s);
        }
        else {
            foreach (var s in secondaries)
                probeCapacity += MaxRateOf(s);
        }

        probeCapacity = (long)(probeCapacity * _debugBandwidthMultiplier);
        var capacity = Math.Max(estimatedCapacity, probeCapacity);
        _lastInboundProbeNote = reprobe && capacity > estimatedCapacity ? "reprobe" : "";
        return capacity;
    }

    internal static bool ShouldReprobe(
        HealthVerdict downlinkVerdict,
        HealthVerdict decoderVerdict,
        int calmTicks,
        int reprobeCalmStreak,
        Moment? lastCeilingDownAt,
        int probeFailures,
        Moment now,
        BandwidthEstimatorConfig config)
    {
        if (downlinkVerdict != HealthVerdict.Good || decoderVerdict == HealthVerdict.Bad)
            return false;
        if (calmTicks < reprobeCalmStreak)
            return false;
        if (lastCeilingDownAt is { } downAt) {
            var cooldownSec = Math.Min(
                config.MaxProbeCooldownSec,
                config.BaseProbeCooldownSec * Math.Pow(config.ProbeCooldownGrowth, probeFailures));
            if ((now - downAt).TotalSeconds < cooldownSec)
                return false;
        }
        return true;
    }

    private string ReprobeCooldownNote(Moment now)
    {
        if (_inboundBwEstimator.LastCeilingDownAt is not { } downAt)
            return "";
        var config = _inboundBwEstimator.Config;
        var cooldownSec = Math.Min(
            config.MaxProbeCooldownSec,
            config.BaseProbeCooldownSec * Math.Pow(config.ProbeCooldownGrowth, _inboundBwEstimator.ProbeFailures));
        var remaining = cooldownSec - (now - downAt).TotalSeconds;
        return remaining > 0 ? $"reprobe cooldown {remaining:F0}s" : "";
    }

    private static long MaxRateOf(StreamAllocationRequest s)
    {
        if (s.PredictedRatesByLayer.Count == 0)
            return 0;
        return s.PredictedRatesByLayer[s.EffectiveLayerCountCap - 1];
    }

    private static long FloorRateOf(StreamAllocationRequest s)
    {
        if (s.PredictedRatesByLayer.Count == 0)
            return 0;
        return s.PredictedRatesByLayer[0];
    }

    private static long ComputeDecayedObservedRate(PlaybackStatsState? prev, long currentRate)
    {
        var current = Math.Max(0, currentRate);
        if (prev is null)
            return current;
        var elapsedSec = Math.Max(0, prev.LastSeen.Elapsed.TotalSeconds);
        var decayed = (long)(prev.ObservedPeakByteRate * Math.Pow(ObservedRateDecayPerSecond, elapsedSec));
        return Math.Max(decayed, current);
    }

    private static VideoSize GetTopVideoSize(VideoSourceKind sourceKind)
    {
        var ladder = VideoRecorder.BuildLadder(sourceKind);
        return ladder[^1].Size;
    }

    private static VideoSize GetDesiredVideoSize(PlaybackStats snapshot, VideoSourceKind sourceKind)
    {
        var size = snapshot.RenderVideoSize;
        return size == VideoSize.None ? GetTopVideoSize(sourceKind) : size;
    }

    private static VideoSize GetDesiredVideoSize(
        PlaybackStats snapshot,
        VideoSourceKind sourceKind,
        bool hasDimensions)
        => hasDimensions || snapshot.Priority != PlaybackStreamPriority.Primary
            ? GetDesiredVideoSize(snapshot, sourceKind)
            : VideoSize.None;

    internal static int GetBestLayerFor(VideoSourceKind sourceKind, VideoSize desiredSize)
    {
        var ladder = VideoRecorder.BuildLadder(sourceKind);
        if (desiredSize == VideoSize.None)
            return ladder.Count - 1;

        // Standard ABR rule: smallest layer ≥ desired (never serve a layer
        // smaller than the display needs). Falls back to the largest layer
        // when the display wants more pixels than the ladder offers.
        // Nearest-by-absolute-distance biased screencast toward L0 because the
        // ladder's two rungs (W960, W1920) are far apart and a typical
        // modal-sized screencast tile rounds DOWN under that rule.
        var desiredWidth = desiredSize.LongSide();
        for (var i = 0; i < ladder.Count; i++) {
            if (ladder[i].Width >= desiredWidth)
                return i;
        }
        return ladder.Count - 1;
    }

    private static PlaybackStats WithRenderFallback(
        PlaybackStats snapshot,
        PlaybackStatsState? previous)
    {
        if (snapshot.RenderCssLongSide > 0 || snapshot.RenderDevicePixelRatio > 0 || previous is null)
            return snapshot;
        return snapshot with {
            RenderCssLongSide = previous.Snapshot.RenderCssLongSide,
            RenderDevicePixelRatio = previous.Snapshot.RenderDevicePixelRatio,
            Priority = previous.Snapshot.Priority,
        };
    }

    private List<KeyValuePair<StreamId, PlaybackStatsState>> GetFreshPlaybackEntries()
    {
        lock (_playbackLock) {
            var isPaused = _isBackground
                || _currentPanelMode is VideoPanelMode.Hidden or VideoPanelMode.Collapsed;
            if (!isPaused) {
                var staleStreamIds = _playbackByStream
                    .Where(x => x.Value.LastSeen.Elapsed > PlaybackHealthTtl)
                    .Select(x => x.Key)
                    .ToArray();
                foreach (var streamId in staleStreamIds) {
                    // A player that goes silent while its stream is still being
                    // delivered is a stall: pruning it here also drops its bytes
                    // from the estimator's input, so the ceiling stops tracking
                    // reality. Report before the evidence is gone.
                    if (TryStartStallReportLocked(streamId))
                        _pendingStallNotes[streamId] = "stats-silent";
                    _playbackByStream.Remove(streamId);
                    _playbackStartedAt.Remove(streamId);
                    _playbackLastEvalAt.Remove(streamId);
                }
            }
            return _playbackByStream.ToList();
        }
    }

    private string GetLayerCapTag(
        string streamId,
        int layerId,
        IReadOnlyList<KeyValuePair<StreamId, PlaybackStatsState>> entries)
    {
        PlaybackStatsState? state = null;
        foreach (var (sid, s) in entries) {
            if (sid.Value == streamId) {
                state = s;
                break;
            }
        }
        return GetLayerCapTag(
            _decoderCapState.Caps.ContainsKey(streamId),
            layerId,
            state?.RequestedLayerCount ?? int.MaxValue,
            _thermalCap.MaxPlaybackLayerCount,
            _debugMaxPlaybackLayerCount ?? int.MaxValue);
    }

    internal static string GetLayerCapTag(
        bool hasDecoderCap,
        int layerId,
        int demandCap,
        int thermalCap,
        int debugCap)
    {
        // Names whichever cap actually bound the layer. This used to collapse to
        // "decoder" vs "bw", so a render-demand or thermal clamp read as a
        // bandwidth shortfall and pointed triage at the wrong subsystem.
        if (hasDecoderCap)
            return "decoder";

        // Below the clamp means the allocator ran out of capacity; at the clamp
        // means the clamp itself is what's holding the layer down.
        var layerCountCap = Math.Min(Math.Min(demandCap, thermalCap), debugCap);
        if (layerId < layerCountCap - 1)
            return "bw";
        if (debugCap < Math.Min(demandCap, thermalCap))
            return "debug";
        if (thermalCap < demandCap)
            return "thermal";

        return "demand";
    }

    private bool TryStartStallReportLocked(StreamId streamId)
    {
        if (_lastStallReportAt.TryGetValue(streamId, out var last) && last.Elapsed < StallReportCooldown)
            return false;

        _lastStallReportAt[streamId] = CpuTimestamp.Now;
        return true;
    }

    private string TakePendingStallNotes()
    {
        lock (_playbackLock) {
            if (_pendingStallNotes.Count == 0)
                return "";

            var note = string.Join(", ", _pendingStallNotes.Select(x => $"{ShortStreamId(x.Key.Value)}:{x.Value}"));
            _pendingStallNotes.Clear();
            return note;
        }
    }

    private void RefreshPlaybackEntriesLastSeenLocked()
    {
        var now = CpuTimestamp.Now;
        foreach (var (streamId, state) in _playbackByStream.ToArray())
            _playbackByStream[streamId] = state with { LastSeen = now };
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
            if (requested is not null && requested.TryGetValue(hint.StreamId, out var q))
                await JS
                    .InvokeVoidAsync(jsMethod, cancellationToken, hint.StreamId, q.LayerId)
                    .ConfigureAwait(false);
            else
                await JS
                    .InvokeVoidAsync(jsMethod, cancellationToken, hint.StreamId, null)
                    .ConfigureAwait(false);
        }
    }

    private async Task ClearRequestedReceiveQualityRegistry(
        IReadOnlyList<PlaybackStreamHint> streamHints,
        CancellationToken cancellationToken)
    {
        var jsMethod = $"{BlazorUIAppModule.ImportName}.setRequestedReceiveQuality";
        foreach (var hint in streamHints)
            await JS.InvokeVoidAsync(jsMethod, cancellationToken, hint.StreamId, null).ConfigureAwait(false);
    }

    private static ApiMap<string, ReceiveQuality> BuildLayerCapQuality(
        int? layerCount,
        IReadOnlyList<PlaybackStreamHint> hints)
    {
        var map = new ApiMap<string, ReceiveQuality>();
        var quality = layerCount is { } count
            ? new ReceiveQuality(Math.Max(0, count - 1))
            : ReceiveQuality.Default;
        foreach (var hint in hints)
            map[hint.StreamId] = quality;
        return map;
    }

    private static int ApplyLayerCountConstraint(int layerCount, int? maxLayerCount)
        => maxLayerCount is { } max ? Math.Clamp(layerCount, 1, max) : layerCount;

    // Nested types

    /// <summary>
    /// What the receiver asked for, and which cap ended up binding the layer.
    /// </summary>
    public readonly record struct InboundDemand(
        VideoSize DesiredSize,
        int RequestedLayerCount,
        double RenderCssLongSide,
        double RenderDevicePixelRatio,
        string CapTag);

    public sealed record PlaybackQualitySnapshot(
        long EstimatedCapacityBytesPerSec,
        double AggregateHealth,
        IReadOnlyDictionary<string, int> Verdicts,
        IReadOnlyDictionary<string, PlaybackStreamSignals> Signals,
        double PlaybackRateEma = 1,
        double DropRatio = 0)
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

    public sealed record PlaybackStreamHint(string StreamId, int CurrentLayerId);

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

    public static class PlaybackVerdictClassifier
    {
        public static int Classify(
            double bufferSpanMsEma,
            PlaybackThresholds t)
        {
            if (bufferSpanMsEma > 0 && bufferSpanMsEma <= t.BufferDurationTooHighMs)
                return 1;

            return 0;
        }
    }

    private sealed record PlaybackStatsState(
        VideoSourceKind SourceKind,
        PlaybackStats Snapshot,
        int Verdict,
        CpuTimestamp LastSeen,
        long ObservedPeakByteRate,
        // Per-stream layer count requested for this allocation cycle (1-based).
        int RequestedLayerCount,
        VideoSize DesiredVideoSize);

    private static string ShortStreamId(string streamId)
    {
        if (string.IsNullOrEmpty(streamId)) return "";
        return streamId.Length <= 6 ? streamId : streamId[..6];
    }
}
