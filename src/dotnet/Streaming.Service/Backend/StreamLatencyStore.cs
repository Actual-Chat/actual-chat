using ActualChat.Diagnostics;
using ActualChat.Video;
using ActualLab.Diagnostics;

namespace ActualChat.Streaming;

/// <summary>
/// Node-local store for video stream latency/quality/keyframe state.
/// Shared between LiveVideoBackend (control methods) and VideoStreamingBackend (data path).
/// </summary>
public sealed class StreamLatencyStore(IServiceProvider services)
{
    internal readonly ConcurrentDictionary<StreamId, StreamLatencyState> LatencyStates = new();
    internal readonly ConcurrentDictionary<StreamId, bool> KeyFrameRequests = new();
    internal readonly ConcurrentDictionary<StreamId, CpuTimestamp> LastKeyFrameRequestTime = new();

    private MomentClockSet Clocks { get; } = services.Clocks();
    private ILogger Log { get; } = services.LogFor<StreamLatencyStore>();
    private ILogger? DebugLog => Log.IfEnabled(LogLevel.Debug);

    public int GetPeerMaxTemporalLayer(StreamId streamId, string peerId)
        => LatencyStates.TryGetValue(streamId, out var state)
            ? state.GetPeerMaxTemporalLayer(peerId)
            : int.MaxValue;

    public int GetPeerMaxSpatialLayer(StreamId streamId, string peerId)
        => LatencyStates.TryGetValue(streamId, out var state)
            ? state.GetPeerMaxSpatialLayer(peerId)
            : int.MaxValue;

    public int DecrementPeerEgressFallback(StreamId streamId, string peerId)
        => LatencyStates.TryGetValue(streamId, out var state)
            ? state.DecrementPeerEgressFallback(peerId)
            : int.MaxValue;

    public void RestorePeerEgressFallback(StreamId streamId, string peerId)
    {
        if (LatencyStates.TryGetValue(streamId, out var state))
            state.RestorePeerEgressFallback(peerId);
    }

    public CpuTimestamp GetPeerEgressFallbackSetAt(StreamId streamId, string peerId)
        => LatencyStates.TryGetValue(streamId, out var state)
            ? state.GetPeerEgressFallbackSetAt(peerId)
            : default;

    public bool HasPeerEgressFallback(StreamId streamId, string peerId)
        => LatencyStates.TryGetValue(streamId, out var state)
            && state.HasPeerEgressFallback(peerId);

    public bool IsPeerVisible(StreamId streamId, string peerId)
        => !LatencyStates.TryGetValue(streamId, out var state)
            || state.IsPeerVisible(peerId);

    public void RegisterStreamLatencyState(StreamId streamId, ChatId chatId, Moment beginsAt, VideoFormat format, StreamKind kind)
        => LatencyStates[streamId] = new StreamLatencyState(chatId, beginsAt, format, kind, services.StateFactory(), Log);

    public void RecordFrameBytes(StreamId streamId, int byteCount, int spatialLayerId = 0)
    {
        if (LatencyStates.TryGetValue(streamId, out var state))
            state.RecordFrameBytes(byteCount, spatialLayerId);
    }

    public void UpdateMaxQuality(StreamId streamId, int sourceWidth, int sourceHeight)
    {
        if (LatencyStates.TryGetValue(streamId, out var state))
            state.UpdateMaxQuality(sourceWidth, sourceHeight);
    }

    public void OnStreamExpire(StreamId streamId)
    {
        LatencyStates.TryRemove(streamId, out _);
        KeyFrameRequests.TryRemove(streamId, out _);
        LastKeyFrameRequestTime.TryRemove(streamId, out _);
    }

    public void ReportPeerLatency(StreamId streamId, string peerId, VideoLatencyReport report)
    {
        // PeerLatencyState.RecordLatency is still positional — unpack here.
        // Null render-quality becomes the -1 sentinel the inner helper expects.
        var renderQualityLevel = report.RenderQuality is { } level ? (int)level : -1;
        if (LatencyStates.TryGetValue(streamId, out var latencyState)) {
            // Hint-only mode: clients that haven't rendered a frame yet (canvas
            // resize fires before the first decoded frame) send StreamOffsetMs<0
            // to update render-cap + visibility without polluting the latency
            // sample window. Same code path as the negative-latency clock-skew
            // branch below, but skips the latency-arithmetic + log line.
            if (report.StreamOffsetMs < 0) {
                latencyState.RecordPeerLatency(peerId, latencyMs: -1f,
                    medianDecodeTimeMs: -1f, bufferDepth: -1, bufferSpanMs: -1f, renderQualityLevel, report.IsVisible);
                return;
            }
            var latency = Clocks.ServerClock.Now - (latencyState.StartedAt + TimeSpan.FromMilliseconds(report.StreamOffsetMs));
            if (latency > TimeSpan.Zero) {
                AppMeters.VideoLatency.Record(latency.TotalMilliseconds);
                DebugLog?.LogDebug("ReportPeerLatency: #{StreamId}, PeerId={PeerId}, StreamOffsetMs={StreamOffsetMs:F0}, LatencyMs={LatencyMs:F0}, DecodeMs={DecodeMs:F1}, BufDepth={BufDepth}, BufSpanMs={BufSpanMs:F0}, RenderLvl={RenderLvl}, Visible={Visible}",
                    streamId, peerId, report.StreamOffsetMs, latency.TotalMilliseconds, report.MedianDecodeTimeMs, report.BufferDepth, report.BufferSpanMs, renderQualityLevel, report.IsVisible);
                latencyState.RecordPeerLatency(peerId, (float)latency.TotalMilliseconds,
                    (float)report.MedianDecodeTimeMs, report.BufferDepth, (float)report.BufferSpanMs, renderQualityLevel, report.IsVisible);
            }
            else {
                DebugLog?.LogDebug("ReportPeerLatency: #{StreamId}, PeerId={PeerId}, negative latency={LatencyMs:F0}ms (clock skew?), applying render hint + visibility only",
                    streamId, peerId, latency.TotalMilliseconds);
                // Skip the polluted latency sample, but still record the render hint
                // and visibility flag — both are independent of the clock skew and
                // drive filter behavior (cap selection + stall suppression).
                latencyState.RecordPeerLatency(peerId, latencyMs: -1f,
                    medianDecodeTimeMs: -1f, bufferDepth: -1, bufferSpanMs: -1f, renderQualityLevel, report.IsVisible);
            }
            return;
        }
        Log.LogWarning("ReportPeerLatency: No latency state for stream #{StreamId}", streamId);
    }

    // Latency state classes

    public sealed class PeerLatencyState
    {
        private readonly Queue<float> _samples = new();
        private readonly Lock _lock = new();
        private readonly CpuTimestamp _createdAt;

        public PeerLatencyState() : this(CpuTimestamp.Now) { }

        internal PeerLatencyState(CpuTimestamp createdAt)
            => _createdAt = createdAt;

        public float MedianLatencyMs { get; private set; }
        public float BaselineLatencyMs { get; private set; } = -1;
        public float MedianDecodeTimeMs { get; private set; } = -1;
        public int BufferDepth { get; private set; } = -1;
        public float BufferSpanMs { get; private set; } = -1;
        public int MaxTemporalLayer { get; private set; } = int.MaxValue;
        public int MaxSpatialLayer { get; private set; } = int.MaxValue;
        // Render-hint cap: client-declared per-peer based on its actual render dims.
        // `int.MaxValue` = not hinted (no cap). Updated from RecordLatency's optional
        // renderQualityLevel param.
        public int RenderHintSpatialLayer { get; private set; } = int.MaxValue;
        // Egress-fallback cap: set by the fan-out filter when the peer stalls on write
        // or can't anchor a keyframe on its current selected layer. Tightest of the
        // three caps. Restored via RestoreEgressFallback after EgressRecoveryWindow
        // of uneventful delivery.
        public int EgressFallbackSpatialLayer { get; private set; } = int.MaxValue;
        public CpuTimestamp EgressFallbackSetAt { get; private set; }
        // Client-declared tab visibility. When false (document.visibilityState ===
        // 'hidden'), the fan-out filter should suppress egress-stall handling —
        // the consumer simply isn't pulling, and skipping ahead or decrementing
        // caps now would penalize the session when the tab comes back.
        public bool IsVisible { get; private set; } = true;
        // Effective cap applied to fan-out: the tightest of latency-derived, render-hint,
        // and egress-fallback caps. Consumers call this via StreamLatencyStore.GetPeerMaxSpatialLayer.
        public int EffectiveMaxSpatial =>
            Math.Min(Math.Min(MaxSpatialLayer, RenderHintSpatialLayer), EgressFallbackSpatialLayer);
        public bool IsWarmedUp => _createdAt.Elapsed >= Constants.Video.PeerWarmupDuration;

        // Delta-from-baseline congestion detection. A peer with a permanently high but
        // stable latency (cross-continent, jitter buffer, etc.) is NOT "slow" — only
        // a significant rise above its own baseline is. During warmup the baseline
        // has not been established yet, so we fall back to the absolute threshold.
        public bool IsNetworkSlow =>
            BaselineLatencyMs > 0
                ? MedianLatencyMs > BaselineLatencyMs + Constants.Video.BaselineLatencyRiseAbsoluteMs
                  && MedianLatencyMs > BaselineLatencyMs * Constants.Video.BaselineLatencyRiseMultiplier
                : MedianLatencyMs > Constants.Video.HighLatencyThresholdMs;

        public bool IsNetworkFast =>
            BaselineLatencyMs > 0
                ? MedianLatencyMs < BaselineLatencyMs + Constants.Video.BaselineLatencyFastMarginMs
                : MedianLatencyMs < Constants.Video.LowLatencyThresholdMs;

        /// <summary>
        /// True when high latency is caused by receiver-side issues (slow decoder or buffer bloat)
        /// rather than network/sender problems.
        /// </summary>
        public bool IsReceiverBound =>
            MedianDecodeTimeMs > Constants.Video.HighDecodeTimeThresholdMs
            || BufferDepth > Constants.Video.HighBufferDepthThreshold;

        public void RecordLatency(float latencyMs, float medianDecodeTimeMs = -1, int bufferDepth = -1, float bufferSpanMs = -1,
            int renderQualityLevel = -1, bool isVisible = true)
        {
            // Render hint is independent of warmup — the client knows its render size
            // even before playback warms up, and a wrong-sized delivery for a sidebar
            // tile wastes bandwidth from frame #1. Apply immediately when provided,
            // even when latencyMs is -1 (caller extracted render hint from a report
            // whose latency was clock-skewed negative).
            if (renderQualityLevel >= 0)
                RenderHintSpatialLayer = MapRenderLevelToSpatialLayer((VideoQualityLevel)renderQualityLevel);
            // Visibility: always applied, regardless of warmup / latency validity.
            IsVisible = isVisible;

            // Skip sample enqueue for sentinel/negative latency. Caller may pass -1
            // when only the render hint is usable.
            if (latencyMs < 0)
                return;

            // Discard samples during warmup to prevent initial-buffer latency from contaminating the median
            if (!IsWarmedUp)
                return;

            lock (_lock) {
                _samples.Enqueue(latencyMs);
                while (_samples.Count > Constants.Video.LatencyHistorySize)
                    _samples.Dequeue();

                // Compute median
                var sorted = _samples.OrderBy(x => x).ToList();
                var mid = sorted.Count / 2;
                MedianLatencyMs = sorted.Count % 2 == 0
                    ? (sorted[mid - 1] + sorted[mid]) / 2f
                    : sorted[mid];

                // Update baseline EMA only while the peer is healthy — freezing the
                // baseline during congestion prevents it from drifting up and masking
                // the problem. First post-warmup sample seeds the baseline outright.
                if (!IsNetworkSlow) {
                    if (BaselineLatencyMs < 0)
                        BaselineLatencyMs = MedianLatencyMs;
                    else {
                        var alpha = Constants.Video.BaselineLatencyEmaAlpha;
                        BaselineLatencyMs = (1 - alpha) * BaselineLatencyMs + alpha * MedianLatencyMs;
                    }
                }

                // Update diagnostics if provided (>= 0 means client sent the value)
                if (medianDecodeTimeMs >= 0)
                    MedianDecodeTimeMs = medianDecodeTimeMs;
                if (bufferDepth >= 0)
                    BufferDepth = bufferDepth;
                if (bufferSpanMs >= 0)
                    BufferSpanMs = bufferSpanMs;

                // Compute per-peer layer caps using delta-from-baseline semantics.
                // A peer with a stable high baseline (e.g. cross-continent, flat 300ms)
                // is NOT congested — congestion shows as a rise ABOVE baseline. So we
                // key caps off IsNetworkSlow / IsNetworkFast (already delta-aware) and
                // fall back to absolute thresholds only during warmup (baseline unset).
                var slow = IsNetworkSlow;
                var fast = IsNetworkFast;

                // Temporal: only drop enhancement layers under congestion.
                MaxTemporalLayer = slow ? 0 : int.MaxValue;

                // Spatial: ladder-agnostic. Slow → base only; fast → uncapped (server filter
                // clamps to producer's observedMaxSpatial, whatever that turns out to be);
                // borderline → mid (~spatial=1). Hard-coding `fast → 2` pinned receivers to
                // the 3rd layer of any ladder, which silently caps a 4-tier 1080p ladder at
                // its 720p tier even on a fast network. int.MaxValue lets observedMax decide.
                MaxSpatialLayer = slow ? 0 : fast ? int.MaxValue : 1;
            }
        }

        // VideoQualityLevel → spatial layer. Render hint answers "what's the smallest
        // layer that still fills the receiver's canvas?" — base (spatial=0) is typical
        // ladder's 180p, useful only for thumbnails/grid; a Low-bucketed 640x360 tile
        // still wants 360p content (spatial=1), not a 180p upscale. Mapping Low→0
        // pinned sidebar viewers to base even on capable networks. Reserve spatial=0
        // for latency/egress-driven drop, not render size.
        private static int MapRenderLevelToSpatialLayer(VideoQualityLevel level)
            => level switch {
                VideoQualityLevel.Low => 1,     // sidebar / small tile — mid tier (~360p)
                VideoQualityLevel.Medium => 1,
                VideoQualityLevel.High => 2,    // ~720p target
                // Full, Ultra — uncapped. Producer's observedMaxSpatial decides; with
                // a 4-tier 1080p ladder this naturally resolves to spatial=3.
                _ => int.MaxValue,
            };

        // Fan-out calls this when the peer stalls on write or walks past retention
        // without finding a keyframe on its current selected spatial layer. Drops
        // the egress floor by one step below the current EffectiveMaxSpatial; never
        // below zero. Returns the new egress floor.
        internal int DecrementEgressFallback()
        {
            lock (_lock) {
                var current = EffectiveMaxSpatial;
                // EffectiveMaxSpatial could be int.MaxValue on a peer with no other
                // caps set — treat that as "assume 2 and decrement from there" so we
                // still move the needle.
                var effective = current == int.MaxValue ? 2 : current;
                var newFloor = Math.Max(0, effective - 1);
                EgressFallbackSpatialLayer = newFloor;
                EgressFallbackSetAt = CpuTimestamp.Now;
                return newFloor;
            }
        }

        // Clears the egress floor. Caller should only invoke this after a window of
        // uneventful delivery at the reduced layer (see EgressRecoveryWindow).
        internal void RestoreEgressFallback()
        {
            lock (_lock) {
                EgressFallbackSpatialLayer = int.MaxValue;
            }
        }
    }

    public sealed class StreamLatencyState(
        ChatId chatId,
        Moment startedAt,
        VideoFormat format,
        StreamKind kind,
        StateFactory stateFactory,
        ILogger log)
    {
        private ILogger Log { get; } = log;
        private ILogger? DebugLog { get; } = log.IfEnabled(LogLevel.Debug);

        public ChatId ChatId { get; } = chatId;
        public Moment StartedAt { get; } = startedAt;
        public string Codec { get; } = format.Codec;
        public StreamKind Kind { get; } = kind;
        // Screencast starts at Full so text is readable from the first keyframe.
        // Camera starts at High — faces/torsos look fine there and 1080p webcam
        // is wasteful when the user isn't the focus of attention.
        public MutableState<VideoQualityPreset> QualityPreset { get; } = stateFactory.NewMutable(
            kind == StreamKind.Screencast ? VideoQualityPreset.Full : VideoQualityPreset.High);

        // Cap max quality to what the SOURCE (capture) can provide — prevents wasteful
        // upscaling. Sender's Width/Height in VideoFormat is the encoder config which
        // starts below source when adaptation is active; use SourceWidth/SourceHeight
        // instead so we know the real headroom. Legacy peers that don't populate source
        // dims send 0 → fall back to encoder dims.
        //
        // Match by **pixel count with 10% tolerance**. Real-world capture dimensions
        // drift from nominal (e.g. getDisplayMedia on a 4K monitor reports 3840x2088
        // on macOS / 3840x2100 on Windows — never 2160 because of the OS chrome;
        // webcams may report 1920x1079 or similar). Strict Width/Height matches reject
        // these. The 90%-of-tier rule lets such sources qualify for their natural tier.
        private static readonly long UltraPixelThreshold = (long)(3840 * 2160 * 0.9);   // ~7.46M
        private static readonly long FullPixelThreshold = (long)(1920 * 1080 * 0.9);    // ~1.87M
        private static readonly long HighPixelThreshold = (long)(1280 * 720 * 0.9);     // ~0.83M
        private static readonly long MediumPixelThreshold = (long)(960 * 540 * 0.9);    // ~0.47M
        // Mutable — UpdateMaxQuality raises the ceiling mid-stream when keyframes
        // report larger source dims (e.g. screencast window resized to full-screen 4K).
        // Note on monotonicity: only promoted upward. We don't lower the ceiling if the
        // source later shrinks — that would risk oscillation (window resize events can
        // be bursty) and the encoder can always be asked to produce smaller than its
        // ceiling. Quality adaptation still steps down based on latency independently.
        private VideoQualityLevel _maxQuality =
            ComputeMaxQuality(format.SourceWidth > 0 ? format.SourceWidth : format.Width,
                              format.SourceHeight > 0 ? format.SourceHeight : format.Height);

        public void UpdateMaxQuality(int sourceWidth, int sourceHeight)
        {
            var newMax = ComputeMaxQuality(sourceWidth, sourceHeight);
            if (newMax < _maxQuality) { // lower enum value = higher quality
                var oldMax = _maxQuality;
                _maxQuality = newMax;
                Log.LogInformation(
                    "UpdateMaxQuality: raised ceiling {OldMax} -> {NewMax} on source {W}x{H}",
                    oldMax, newMax, sourceWidth, sourceHeight);
            }
        }

        private static VideoQualityLevel ComputeMaxQuality(int width, int height)
        {
            var pixels = (long)width * height;
            return pixels switch
            {
                var p when p >= UltraPixelThreshold => VideoQualityLevel.Ultra,
                var p when p >= FullPixelThreshold => VideoQualityLevel.Full,
                var p when p >= HighPixelThreshold => VideoQualityLevel.High,
                var p when p >= MediumPixelThreshold => VideoQualityLevel.Medium,
                _ => VideoQualityLevel.Low,
            };
        }

        private readonly ConcurrentDictionary<string, PeerLatencyState> _peers = new();
        private readonly Lock _evaluationLock = new();

        private CpuTimestamp _lastQualityChangeAt = CpuTimestamp.Now;
        private CpuTimestamp _lastEvaluationAt;

        // Throughput measurement
        private long _totalBytesReceived;
        private long _bytesAtLastCheck;
        private CpuTimestamp _lastThroughputCheckAt = CpuTimestamp.Now;
        private CpuTimestamp _lastByteReceivedAt = CpuTimestamp.Now;
        private int _consecutiveHighThroughputChecks;
        // Highest SpatialLayerId observed across all incoming frames. >0 means
        // simulcast is active — multi-encoder bytes legitimately sum past any
        // single-layer target, so OVER-DELIVERY (which models single-encoder
        // VBR overshoot) doesn't apply. Monotonic; never resets.
        private int _maxObservedSpatialLayer;

        public int GetPeerMaxTemporalLayer(string peerId)
            => _peers.TryGetValue(peerId, out var state) ? state.MaxTemporalLayer : int.MaxValue;

        public int GetPeerMaxSpatialLayer(string peerId)
            => _peers.TryGetValue(peerId, out var state) ? state.EffectiveMaxSpatial : int.MaxValue;

        // Test-only: inject a pre-warmed PeerLatencyState so RecordLatency doesn't
        // discard samples during warmup. Production code uses the default-ctor path
        // via RecordPeerLatency's GetOrAdd.
        internal PeerLatencyState GetOrAddWarmedPeerForTests(string peerId)
            => _peers.GetOrAdd(peerId, _ => new PeerLatencyState(CpuTimestamp.Now - TimeSpan.FromMinutes(1)));

        public int DecrementPeerEgressFallback(string peerId)
            => _peers.TryGetValue(peerId, out var state) ? state.DecrementEgressFallback() : int.MaxValue;

        public void RestorePeerEgressFallback(string peerId)
        {
            if (_peers.TryGetValue(peerId, out var state))
                state.RestoreEgressFallback();
        }

        public CpuTimestamp GetPeerEgressFallbackSetAt(string peerId)
            => _peers.TryGetValue(peerId, out var state) ? state.EgressFallbackSetAt : default;

        public bool HasPeerEgressFallback(string peerId)
            => _peers.TryGetValue(peerId, out var state) && state.EgressFallbackSpatialLayer != int.MaxValue;

        public void RecordPeerLatency(string peerId, float latencyMs,
            float medianDecodeTimeMs = -1, int bufferDepth = -1, float bufferSpanMs = -1,
            int renderQualityLevel = -1, bool isVisible = true)
        {
            var peer = _peers.GetOrAdd(peerId, _ => new PeerLatencyState());
            peer.RecordLatency(latencyMs, medianDecodeTimeMs, bufferDepth, bufferSpanMs, renderQualityLevel, isVisible);
            DebugLog?.LogDebug(
                "RecordPeerLatency: PeerId={PeerId}, LatencyMs={LatencyMs:F0}, MedianMs={MedianMs:F0}, ReceiverBound={ReceiverBound}, MaxSpatial={MaxSpatial}, Visible={Visible}",
                peerId, latencyMs, peer.MedianLatencyMs, peer.IsReceiverBound, peer.EffectiveMaxSpatial, peer.IsVisible);

            // Throttle evaluation to QualityDecisionInterval
            if (_lastEvaluationAt.Elapsed >= Constants.Video.QualityDecisionInterval)
                EvaluateQuality();
        }

        public bool IsPeerVisible(string peerId)
            => !_peers.TryGetValue(peerId, out var state) || state.IsVisible;

        public void RecordFrameBytes(int byteCount, int spatialLayerId = 0)
        {
            Interlocked.Add(ref _totalBytesReceived, byteCount);
            _lastByteReceivedAt = CpuTimestamp.Now;
            // Track observed simulcast width so EvaluateQuality can skip the
            // single-encoder OVER-DELIVERY check once multi-encoder is live.
            if (spatialLayerId > _maxObservedSpatialLayer) {
                // Lock-free max: CAS until we lose to a higher writer.
                int observed;
                do {
                    observed = Volatile.Read(ref _maxObservedSpatialLayer);
                    if (spatialLayerId <= observed) break;
                } while (Interlocked.CompareExchange(ref _maxObservedSpatialLayer, spatialLayerId, observed) != observed);
            }
        }

        public bool IsSimulcastActive => Volatile.Read(ref _maxObservedSpatialLayer) > 0;

        internal void EvaluateQuality()
        {
            lock (_evaluationLock) {
                _lastEvaluationAt = CpuTimestamp.Now;

                var currentQuality = QualityPreset.Value.Level;

                // Throughput-based proactive step-down: detect sender upload saturation
                // Use time span from last check to last byte received (not wall-clock)
                // to avoid undercount when bytes are flushed periodically from remote nodes.
                var elapsedSinceCheck = _lastThroughputCheckAt.Elapsed;
                if (elapsedSinceCheck >= Constants.Video.QualityDecisionInterval) {
                    var currentBytes = Interlocked.Read(ref _totalBytesReceived);
                    var bytesDelta = currentBytes - _bytesAtLastCheck;
                    _bytesAtLastCheck = currentBytes;
                    var lastByteReceivedAt = _lastByteReceivedAt;
                    var measurementSpan = lastByteReceivedAt > _lastThroughputCheckAt
                        ? (lastByteReceivedAt - _lastThroughputCheckAt).TotalSeconds
                        : 0;
                    var measuredBps = measurementSpan > 0.1 ? bytesDelta * 8.0 / measurementSpan : 0;
                    var targetBps = VideoBitrateTable.GetExpectedBitrate(Codec, QualityPreset.Value.Height, Kind);
                    _lastThroughputCheckAt = CpuTimestamp.Now;

                    // Under-delivery step-down removed: measured < target is not a congestion
                    // signal. Encoder output is content-driven (static screens, dark scenes, VAD
                    // framerate cuts all produce < target) and real congestion shows up as latency
                    // rise first (TCP backpressure → socket buffer fills → ACK delay grows), which
                    // the per-peer latency-vs-baseline check below catches directly.
                    //
                    // Over-delivery detection: HW encoder ignoring bitrate cap (e.g. HEVC VBR at 12Mbps vs 4Mbps target).
                    // Disabled when simulcast is active — measured bytes legitimately sum across N concurrent encoders
                    // (~base + extras), each respecting its own cap, while the target only models a single layer.
                    // Without this guard, every simulcast stream trips OVER-DELIVERY on the next evaluation tick.
                    if (!IsSimulcastActive && targetBps > 0 && measuredBps > targetBps * Constants.Video.ThroughputOverDeliveryRatio) {
                        _consecutiveHighThroughputChecks++;
                        if (_consecutiveHighThroughputChecks >= Constants.Video.ThroughputStepDownConsecutiveChecks) {
                            var stepped = VideoQualityPreset.StepDown(currentQuality, Kind);
                            if (stepped != null) {
                                _consecutiveHighThroughputChecks = 0;
                                _lastQualityChangeAt = CpuTimestamp.Now;
                                QualityPreset.Value = stepped;
                                Log.LogInformation(
                                    "EvaluateQuality: OVER-DELIVERY STEP DOWN {OldLevel} -> {NewLevel}, measured={MeasuredKbps:F0}kbps vs target={TargetKbps:F0}kbps (>{Ratio:F1}x)",
                                    currentQuality, stepped.Level, measuredBps / 1000, targetBps / 1000.0, Constants.Video.ThroughputOverDeliveryRatio);
                                return;
                            }
                        }
                    }
                    else
                        _consecutiveHighThroughputChecks = 0;
                }

                var peers = _peers.ToList();
                if (peers.Count == 0)
                    return;

                var networkSlowCount = 0;
                var receiverSlowCount = 0;
                foreach (var (peerId, peer) in peers) {
                    if (!peer.IsNetworkSlow)
                        continue;

                    if (peer.IsReceiverBound) {
                        receiverSlowCount++;
                        DebugLog?.LogDebug(
                            "EvaluateQuality: PeerId={PeerId} receiver-bound (decodeMs={DecodeMs:F1}, bufDepth={BufDepth})",
                            peerId, peer.MedianDecodeTimeMs, peer.BufferDepth);
                    }
                    else {
                        networkSlowCount++;
                        DebugLog?.LogDebug(
                            "EvaluateQuality: PeerId={PeerId} network-slow (medianMs={MedianMs:F0}, baselineMs={BaselineMs:F0})",
                            peerId, peer.MedianLatencyMs, peer.BaselineLatencyMs);
                    }
                }

                var totalSlowCount = networkSlowCount + receiverSlowCount;
                var networkSlowRatio = (float)networkSlowCount / peers.Count;
                var effectiveOutlierRatio = peers.Count <= 3
                    ? Constants.Video.PeerOutlierRatioSmallCall
                    : Constants.Video.PeerOutlierRatio;

                if (networkSlowRatio > effectiveOutlierRatio) {
                    var stepped = VideoQualityPreset.StepDown(currentQuality, Kind);
                    if (stepped != null) {
                        _lastQualityChangeAt = CpuTimestamp.Now;
                        QualityPreset.Value = stepped;
                        Log.LogInformation(
                            "EvaluateQuality: STEP DOWN {OldLevel} -> {NewLevel}, networkSlow={NetworkSlow}, receiverSlow={ReceiverSlow}, total={TotalCount} (threshold={Threshold:F2})",
                            currentQuality, stepped.Level, networkSlowCount, receiverSlowCount, peers.Count, effectiveOutlierRatio);
                    }
                }
                else if (totalSlowCount == 0 && _lastQualityChangeAt.Elapsed >= Constants.Video.QualityHysteresisWindow) {
                    // Step-up only when all peers are unambiguously fast. Borderline
                    // peers (neither slow nor fast — e.g. still building samples)
                    // hold quality but must still reach aggregation below so the
                    // publisher sees ViewerCount/MaxSpatialLayer updates (the
                    // asymmetric-publisher arm path depends on it).
                    var allFast = peers.All(p => p.Value.IsNetworkFast && !p.Value.IsReceiverBound);
                    if (allFast) {
                        // Short-circuit when already at the camera ceiling. Without this,
                        // every 2 s ReportPeerLatency would cycle through StepUp + a SKIP
                        // log line for the same scenario — pure noise once the stream
                        // saturates `_maxQuality`. Lower enum value = higher quality, so
                        // currentQuality at or above the ceiling has Level <= _maxQuality.
                        VideoQualityPreset? stepped = null;
                        if (currentQuality > _maxQuality) {
                            stepped = VideoQualityPreset.StepUp(currentQuality);
                            if (stepped != null && stepped.Level < _maxQuality) {
                                // Stepped past the ceiling — Debug so the path is still
                                // diagnosable while not spamming production logs at info
                                // every tick.
                                DebugLog?.LogDebug(
                                    "EvaluateQuality: SKIP step-up to {Level}, camera max is {MaxLevel}",
                                    stepped.Level, _maxQuality);
                                stepped = null;
                            }
                        }
                        if (stepped != null) {
                            _lastQualityChangeAt = CpuTimestamp.Now;
                            QualityPreset.Value = stepped;
                            Log.LogInformation(
                                "EvaluateQuality: STEP UP {OldLevel} -> {NewLevel}, all peers fast ({TotalCount} peers)",
                                currentQuality, stepped.Level, peers.Count);
                        }
                    }
                }
                else
                    DebugLog?.LogDebug(
                        "EvaluateQuality: HOLD at {Level}, networkSlow={NetworkSlow}, receiverSlow={ReceiverSlow}, total={TotalCount}",
                        currentQuality, networkSlowCount, receiverSlowCount, peers.Count);

                // Aggregate per-peer layer demands into the stream-wide preset so the
                // sender (subscribed via Fusion on GetQualityPreset) knows which
                // simulcast layers are actually required. `Max` semantics — if ANY peer
                // wants layer N, sender must produce it; peers with lower caps are
                // filtered per-peer in VideoStreamFilter.
                var aggregatedMaxSpatial = peers.Max(p => p.Value.EffectiveMaxSpatial);
                var aggregatedMaxTemporal = peers.Max(p => p.Value.MaxTemporalLayer);
                // ViewerCount is the number of peers that have pushed at least one
                // ReportPeerLatency for this stream — i.e. active subscribers. The
                // publisher uses this to decide simulcast activation independent of
                // its own incoming-stream count (covers the asymmetric publisher
                // case where peer P pushes, peer V watches but never pushes itself).
                var withLayers = QualityPreset.Value with {
                    MaxSpatialLayer = aggregatedMaxSpatial,
                    MaxTemporalLayer = aggregatedMaxTemporal,
                    ViewerCount = peers.Count,
                };
                if (withLayers != QualityPreset.Value)
                    QualityPreset.Value = withLayers;
            }
        }
    }
}
