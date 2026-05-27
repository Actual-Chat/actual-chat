using ActualChat.Bandwidth;
using ActualChat.Streaming;
using ActualChat.UI.Blazor.App.Module;

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
    private readonly BandwidthEstimator _inboundBwEstimator;
    private PlaybackQualitySnapshot _playbackSnapshot = PlaybackQualitySnapshot.Empty;
    // Per-stream classifier instances — each owns its own streak counters for
    // Downlink + Decoder. Lazy-created on first OnPlaybackStats per stream.
    private readonly Dictionary<StreamId, ReceiverHealthClassifier> _receiverHealthByStream = new();
    private readonly Dictionary<StreamId, DownlinkHealth> _lastDownlinkHealthByStream = new();
    private readonly Dictionary<StreamId, DecoderHealth> _lastDecoderHealthByStream = new();
    // Sticky decoder cap with edge-triggered demote. Set on Good→Bad
    // transition, cleared on =Good. Marginal holds the last value.
    private readonly DecoderCapState _decoderCapState = new();
    // Snapshot of the previous tick's requested layer per stream, used to
    // detect allocator-driven cap moves for the decision log.
    private readonly Dictionary<string, int> _prevRequestedLayerByStream = new();
    private HealthVerdict _lastAggregateDownlinkVerdict = HealthVerdict.Unknown;

    public BandwidthEstimator InboundBandwidthEstimator => _inboundBwEstimator;
    public PlaybackQualitySnapshot PlaybackSnapshot => _playbackSnapshot;
    public HealthVerdict AggregateDownlinkVerdict => _lastAggregateDownlinkVerdict;
    public IReadOnlyDictionary<StreamId, DownlinkHealth> InboundDownlinkHealthByStream
        => _lastDownlinkHealthByStream;
    public IReadOnlyDictionary<StreamId, DecoderHealth> InboundDecoderHealthByStream
        => _lastDecoderHealthByStream;
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

    private async Task RecomputePlaybackQuality(PlaybackQualityReason reason, CancellationToken cancellationToken)
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
            // OpenTelemetry emission deferred — AppMeters lives in Core.Server
            // (unreferenced from UI.Blazor.App). Wire DTOs will carry the
            // per-leg fields in a follow-up so server-side handlers can record.
            if (streamDownlink.Verdict != HealthVerdict.Unknown
                && (int)streamDownlink.Verdict > (int)aggregateDownlinkVerdict)
                aggregateDownlinkVerdict = streamDownlink.Verdict;
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
        }
        _decoderCapState.PruneStaleStreams(liveStreamIdStrings);

        _lastAggregateDownlinkVerdict = aggregateDownlinkVerdict;
        var downlinkSignal = VerdictToSignal(aggregateDownlinkVerdict);
        var connection = ConnectivityUI.ConnectionInfo.Value;
        _inboundBwEstimator.Tick(connection, SystemClock.Now, sumIncomingBytesPerSec, downlinkSignal);
        var estimatedCapacity = (long)(_inboundBwEstimator.CeilingBps * _debugBandwidthMultiplier);
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
        var capacity = GetAllocationCapacity(estimatedCapacity, primaries, secondaries);
        var decoderLayerCapDict = _decoderCapState.Caps.Count == 0
            ? null
            : _decoderCapState.Caps;
        var requested = VideoQualityAllocator.Allocate(capacity, primaries, secondaries, decoderLayerCapDict);
        var requestedMap = new ApiMap<string, ReceiveQuality>();
        foreach (var (streamId, _) in entries)
            requestedMap[streamId.Value] = requested.GetValueOrDefault(streamId.Value, ReceiveQuality.Lowest);
        // Float (Collapsed) shows only the primary tile — pause every secondary
        // stream. Hide pauses every stream. The server filter drops every frame
        // while Paused is in effect, so this also throttles inbound bandwidth.
        var panelMode = await Hub.ChatVideoUI.GetVideoPanelMode(cancellationToken).ConfigureAwait(false);
        if (panelMode == VideoPanelMode.Hidden) {
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
        var info = new PlaybackQualityInfo(
            estimatedCapacity,
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
                .Select(x => new PlaybackStreamHint(x.Key.Value, Math.Max(0, x.Value.RequestedLayerCount - 1)))
                .ToArray(),
            requestedMap,
            cancellationToken).ConfigureAwait(false);

        // Decision-log entry. Aggregate decoder verdict = worst across
        // streams (mirrors the removed Decoder chip semantics).
        var aggregateDecoderVerdict = HealthVerdict.Unknown;
        foreach (var (_, h) in _lastDecoderHealthByStream) {
            if (h.Verdict == HealthVerdict.Unknown) continue;
            if (aggregateDecoderVerdict == HealthVerdict.Unknown
                || (int)h.Verdict > (int)aggregateDecoderVerdict)
                aggregateDecoderVerdict = h.Verdict;
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
                var capTag = _decoderCapState.Caps.ContainsKey(sid) ? "decoder" : "bw";
                capChange = $"{ShortStreamId(sid)} L{prevLayer}→L{q.LayerId} ({capTag})";
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
            var layerCountCap = Math.Min(entry.Value.RequestedLayerCount, debugCap ?? int.MaxValue);
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

    // Only receiver-side stages (61-90) reflect bandwidth/health on the
    // consumer side — sender + server drops are intentional pacing and don't
    // belong in the receive-side penalty.
    private static double ComputeAggregateReceiveDropRatio(IReadOnlyList<KeyValuePair<StreamId, PlaybackStatsState>> entries)
    {
        long totalDrops = 0;
        long totalPresented = 0;
        foreach (var (_, state) in entries) {
            foreach (var (stage, count) in state.Snapshot.DropTrace) {
                var b = (byte)stage;
                if (b is >= 61 and <= 90)
                    totalDrops += count;
            }
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
        IReadOnlyList<StreamAllocationRequest> secondaries)
    {
        if (_inboundBwEstimator.HasSeenBadSignal)
            return estimatedCapacity;

        // Before the first bad receiver signal, the ceiling is only a seed.
        // Don't let that unproven estimate prevent the probe that would prove
        // a higher capacity. Probe primaries to their requested cap and keep
        // secondaries at floor; if there are no primaries, probe all active
        // streams to their requested caps.
        long probeCapacity = 0;
        if (primaries.Count != 0) {
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
        return Math.Max(estimatedCapacity, probeCapacity);
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
            var isPaused = _currentPanelMode is VideoPanelMode.Hidden or VideoPanelMode.Collapsed;
            if (!isPaused) {
                var staleStreamIds = _playbackByStream
                    .Where(x => x.Value.LastSeen.Elapsed > PlaybackHealthTtl)
                    .Select(x => x.Key)
                    .ToArray();
                foreach (var streamId in staleStreamIds) {
                    _playbackByStream.Remove(streamId);
                    _playbackStartedAt.Remove(streamId);
                    _playbackLastEvalAt.Remove(streamId);
                }
            }
            return _playbackByStream.ToList();
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
