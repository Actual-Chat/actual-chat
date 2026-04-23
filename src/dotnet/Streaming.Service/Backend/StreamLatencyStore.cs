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

    public void RegisterStreamLatencyState(StreamId streamId, ChatId chatId, Moment beginsAt, VideoFormat format, StreamKind kind)
        => LatencyStates[streamId] = new StreamLatencyState(chatId, beginsAt, format, kind, services.StateFactory(), Log);

    public void RecordFrameBytes(StreamId streamId, int byteCount)
    {
        if (LatencyStates.TryGetValue(streamId, out var state))
            state.RecordFrameBytes(byteCount);
    }

    public void OnStreamExpire(StreamId streamId)
    {
        LatencyStates.TryRemove(streamId, out _);
        KeyFrameRequests.TryRemove(streamId, out _);
        LastKeyFrameRequestTime.TryRemove(streamId, out _);
    }

    public void ReportPeerLatency(
        StreamId streamId,
        string peerId,
        double streamOffsetMs,
        double medianDecodeTimeMs = -1,
        int bufferDepth = -1,
        double bufferSpanMs = -1)
    {
        if (LatencyStates.TryGetValue(streamId, out var latencyState)) {
            var latency = Clocks.ServerClock.Now - (latencyState.StartedAt + TimeSpan.FromMilliseconds(streamOffsetMs));
            if (latency > TimeSpan.Zero) {
                AppMeters.VideoLatency.Record(latency.TotalMilliseconds);
                DebugLog?.LogDebug("ReportPeerLatency: #{StreamId}, PeerId={PeerId}, StreamOffsetMs={StreamOffsetMs:F0}, LatencyMs={LatencyMs:F0}, DecodeMs={DecodeMs:F1}, BufDepth={BufDepth}, BufSpanMs={BufSpanMs:F0}",
                    streamId, peerId, streamOffsetMs, latency.TotalMilliseconds, medianDecodeTimeMs, bufferDepth, bufferSpanMs);
                latencyState.RecordPeerLatency(peerId, (float)latency.TotalMilliseconds,
                    (float)medianDecodeTimeMs, bufferDepth, (float)bufferSpanMs);
            }
            else
                DebugLog?.LogDebug("ReportPeerLatency: #{StreamId}, PeerId={PeerId}, negative latency={LatencyMs:F0}ms (clock skew?), skipping",
                    streamId, peerId, latency.TotalMilliseconds);
            return;
        }
        Log.LogWarning("ReportPeerLatency: No latency state for stream #{StreamId}", streamId);
    }

    // Latency state classes

    public sealed class PeerLatencyState
    {
        private readonly Queue<float> _samples = new();
        private readonly Lock _lock = new();
        private readonly CpuTimestamp _createdAt = CpuTimestamp.Now;

        public float MedianLatencyMs { get; private set; }
        public float BaselineLatencyMs { get; private set; } = -1;
        public float MedianDecodeTimeMs { get; private set; } = -1;
        public int BufferDepth { get; private set; } = -1;
        public float BufferSpanMs { get; private set; } = -1;
        public int MaxTemporalLayer { get; private set; } = int.MaxValue;
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

        public void RecordLatency(float latencyMs, float medianDecodeTimeMs = -1, int bufferDepth = -1, float bufferSpanMs = -1)
        {
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

                // Compute max temporal layer based on latency
                if (MedianLatencyMs > Constants.Video.HighLatencyThresholdMs)
                    MaxTemporalLayer = 0; // Base layer only
                else if (MedianLatencyMs > Constants.Video.LowLatencyThresholdMs)
                    MaxTemporalLayer = 0; // Conservative: base layer when borderline
                else
                    MaxTemporalLayer = int.MaxValue; // All layers
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

        // Cap max quality to what the source can actually provide — prevents wasteful upscaling.
        // Ultra (4K) only unlocks when the source reports >= 3840x2160 — typical for
        // getDisplayMedia on a 4K monitor; webcams rarely exceed 1080p.
        private readonly VideoQualityLevel _maxQuality = format switch
        {
            { Width: >= 3840, Height: >= 2160 } => VideoQualityLevel.Ultra,
            { Width: >= 1920, Height: >= 1080 } => VideoQualityLevel.Full,
            { Width: >= 1280, Height: >= 720 } => VideoQualityLevel.High,
            { Width: >= 960, Height: >= 540 } => VideoQualityLevel.Medium,
            _ => VideoQualityLevel.Low,
        };

        private readonly ConcurrentDictionary<string, PeerLatencyState> _peers = new();
        private readonly Lock _evaluationLock = new();

        private CpuTimestamp _lastQualityChangeAt = CpuTimestamp.Now;
        private CpuTimestamp _lastEvaluationAt;

        // Throughput measurement
        private long _totalBytesReceived;
        private long _bytesAtLastCheck;
        private CpuTimestamp _lastThroughputCheckAt = CpuTimestamp.Now;
        private CpuTimestamp _lastByteReceivedAt = CpuTimestamp.Now;
        private int _consecutiveLowThroughputChecks;
        private int _consecutiveHighThroughputChecks;

        public int GetPeerMaxTemporalLayer(string peerId)
            => _peers.TryGetValue(peerId, out var state) ? state.MaxTemporalLayer : int.MaxValue;

        public void RecordPeerLatency(string peerId, float latencyMs,
            float medianDecodeTimeMs = -1, int bufferDepth = -1, float bufferSpanMs = -1)
        {
            var peer = _peers.GetOrAdd(peerId, _ => new PeerLatencyState());
            peer.RecordLatency(latencyMs, medianDecodeTimeMs, bufferDepth, bufferSpanMs);
            DebugLog?.LogDebug(
                "RecordPeerLatency: PeerId={PeerId}, LatencyMs={LatencyMs:F0}, MedianMs={MedianMs:F0}, ReceiverBound={ReceiverBound}",
                peerId, latencyMs, peer.MedianLatencyMs, peer.IsReceiverBound);

            // Throttle evaluation to QualityDecisionInterval
            if (_lastEvaluationAt.Elapsed >= Constants.Video.QualityDecisionInterval)
                EvaluateQuality();
        }

        public void RecordFrameBytes(int byteCount)
        {
            Interlocked.Add(ref _totalBytesReceived, byteCount);
            _lastByteReceivedAt = CpuTimestamp.Now;
        }

        private void EvaluateQuality()
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

                    // Under-delivery step-down applies to webcam only. A camera sensor produces
                    // near-constant entropy, so a webcam encoder lands close to target — sustained
                    // undershoot then reliably signals an uplink bottleneck.
                    // Screencast encoder output is content-driven: static/simple screens compress to
                    // 500–900kbps at 720p regardless of target cap, so a "measured < 50% target"
                    // ratio constantly trips even on a local link with sub-200ms latency.
                    // Latency-based step-down (per-peer median vs baseline, below) remains the
                    // primary congestion detector for screencast.
                    if (Kind != StreamKind.Screencast
                        && targetBps > 0 && measuredBps < targetBps * Constants.Video.ThroughputStepDownRatio) {
                        _consecutiveLowThroughputChecks++;
                        if (_consecutiveLowThroughputChecks >= Constants.Video.ThroughputStepDownConsecutiveChecks) {
                            var stepped = VideoQualityPreset.StepDown(currentQuality, Kind);
                            if (stepped != null) {
                                _consecutiveLowThroughputChecks = 0;
                                _lastQualityChangeAt = CpuTimestamp.Now;
                                QualityPreset.Value = stepped;
                                Log.LogInformation(
                                    "EvaluateQuality: THROUGHPUT STEP DOWN {OldLevel} -> {NewLevel}, measured={MeasuredKbps:F0}kbps vs target={TargetKbps:F0}kbps",
                                    currentQuality, stepped.Level, measuredBps / 1000, targetBps / 1000.0);
                                return;
                            }
                        }
                    }
                    else
                        _consecutiveLowThroughputChecks = 0;

                    // Over-delivery detection: HW encoder ignoring bitrate cap (e.g. HEVC VBR at 12Mbps vs 4Mbps target)
                    if (targetBps > 0 && measuredBps > targetBps * Constants.Video.ThroughputOverDeliveryRatio) {
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
                    var allFast = peers.All(p => p.Value.IsNetworkFast && !p.Value.IsReceiverBound);
                    if (!allFast)
                        return;

                    var stepped = VideoQualityPreset.StepUp(currentQuality);
                    if (stepped != null && stepped.Level < _maxQuality) {
                        Log.LogInformation(
                            "EvaluateQuality: SKIP step-up to {Level}, camera max is {MaxLevel}",
                            stepped.Level, _maxQuality);
                        stepped = null;
                    }
                    if (stepped != null) {
                        _lastQualityChangeAt = CpuTimestamp.Now;
                        QualityPreset.Value = stepped;
                        Log.LogInformation(
                            "EvaluateQuality: STEP UP {OldLevel} -> {NewLevel}, all peers fast ({TotalCount} peers)",
                            currentQuality, stepped.Level, peers.Count);
                    }
                }
                else
                    DebugLog?.LogDebug(
                        "EvaluateQuality: HOLD at {Level}, networkSlow={NetworkSlow}, receiverSlow={ReceiverSlow}, total={TotalCount}",
                        currentQuality, networkSlowCount, receiverSlowCount, peers.Count);
            }
        }
    }
}
