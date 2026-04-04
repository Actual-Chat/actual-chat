using ActualChat.Diagnostics;
using ActualChat.Streaming.Services;
using ActualChat.Video;
using ActualLab.Diagnostics;
using ActualLab.Rpc;

namespace ActualChat.Streaming;

public class VideoStreamingBackend : IVideoStreamingBackend, IDisposable
{
    private readonly StreamStore<VideoFrame> _videoStreams;
    private readonly ConcurrentDictionary<StreamId, StreamLatencyState> _latencyStates = new();
    private readonly ConcurrentDictionary<StreamId, bool> _keyFrameRequests = new();

    private MeshNode ThisNode => field ??= Services.MeshWatcher().ThisNode;
    private IChats Chats => field ??= Services.GetRequiredService<IChats>();
    private IAuthors Authors => field ??= Services.GetRequiredService<IAuthors>();
    private MomentClockSet Clocks => field ??= Services.Clocks();
    private ILiveVideoBackend LiveVideoBackend => field ??= Services.GetRequiredService<ILiveVideoBackend>();
    private ILogger Log => field ??= Services.LogFor(GetType());
    private ILogger? DebugLog => Log.IfEnabled(LogLevel.Debug);

    private IServiceProvider Services { get; }

    public VideoStreamingBackend(IServiceProvider services)
    {
        Services = services;
        var typeFullName = GetType().FullName;
        _videoStreams = new StreamStore<VideoFrame> {
            StreamIdValidator = ValidateStreamId,
            StreamCount = AppMeters.VideoStreamCount,
            ExpirationDelay = Constants.Video.StreamExpirationDelay,
            ReplayTailSize = Constants.Video.ReplayBufferSize,
            OnStreamExpire = OnVideoStreamExpire,
            Log = services.LogFor($"{typeFullName}.VideoStreams"),
        };
    }

    public void Dispose()
        => _videoStreams.Dispose();

    public virtual async Task<RpcStream<VideoFrame>?> GetVideo(StreamId streamId, TimeSpan skipTo, string peerId, CancellationToken cancellationToken)
    {
        Log.LogInformation("GetVideo: #{StreamId}, SkipTo={SkipTo}, PeerId={PeerId}", streamId, skipTo, peerId);
        var stream = await _videoStreams.Get(streamId, cancellationToken).ConfigureAwait(false);
        if (stream == null) {
            Log.LogWarning("GetVideo: #{StreamId} not found in StreamStore", streamId);
            return null;
        }

        // Skip to keyframe on startup and recover from gaps caused by bounded replay dropping frames
        stream = KeyFrameGapFilter(stream, Log, cancellationToken);

        // Per-viewer temporal layer filtering — drop enhancement layers for slow peers
        stream = TemporalLayerFilter(streamId, peerId, stream, cancellationToken);

        // Wrap with pause filtering — when stream is paused by priority queue, drop frames
        if (_latencyStates.TryGetValue(streamId, out var pauseState) && pauseState.ChatId != default)
            stream = PauseAwareFilter(streamId, stream, cancellationToken);

        return RpcStream.New(stream);
    }

    public virtual async Task PushVideo(
        VideoRecord record,
        RpcStream<VideoFrame> videoStream,
        CancellationToken cancellationToken)
    {
        ValidateStreamId(record.StreamId);
        Log.LogTrace(nameof(PushVideo) + ": record #{StreamId} = {Record}", record.StreamId, record);

        var delayedCts = cancellationToken.CreateDelayedTokenSource(Constants.Video.CancellationDelay);
        var delayedCancellationToken = delayedCts.Token;

        try {
            await PushVideoInternal(record, videoStream, delayedCancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception e) when (e is not OperationCanceledException) {
            Log.LogError(e, "PushVideo failed for stream #{StreamId}", record.StreamId);
            throw;
        }
        finally {
            delayedCts.CancelAndDisposeSilently();
        }
    }

    public virtual Task ReportPeerLatency(
        StreamId streamId,
        string peerId,
        double streamOffsetMs,
        double medianDecodeTimeMs = -1,
        int bufferDepth = -1,
        double bufferSpanMs = -1,
        CancellationToken cancellationToken = default)
    {
        if (_latencyStates.TryGetValue(streamId, out var latencyState)) {
            var latency = Clocks.ServerClock.Now - (latencyState.StartedAt + TimeSpan.FromMilliseconds(streamOffsetMs));
            if (latency > TimeSpan.Zero) {
                AppMeters.VideoLatency.Record(latency.TotalMilliseconds);
                DebugLog?.LogDebug("ReportPeerLatency: #{StreamId}, PeerId={PeerId}, StreamOffsetMs={StreamOffsetMs:F0}, LatencyMs={LatencyMs:F0}, DecodeMs={DecodeMs:F1}, BufDepth={BufDepth}, BufSpanMs={BufSpanMs:F0}",
                    streamId, peerId, streamOffsetMs, latency.TotalMilliseconds, medianDecodeTimeMs, bufferDepth, bufferSpanMs);
                latencyState.RecordPeerLatency(peerId, (float)latency.TotalMilliseconds,
                    (float)medianDecodeTimeMs, bufferDepth, (float)bufferSpanMs);
            }
            else {
                DebugLog?.LogDebug("ReportPeerLatency: #{StreamId}, PeerId={PeerId}, negative latency={LatencyMs:F0}ms (clock skew?), skipping",
                    streamId, peerId, latency.TotalMilliseconds);
            }
            return Task.CompletedTask;
        }
        Log.LogWarning("ReportPeerLatency: No latency state for stream #{StreamId}", streamId);
        return Task.CompletedTask;
    }

    public virtual Task RequestKeyFrame(StreamId streamId, CancellationToken cancellationToken = default)
    {
        ValidateStreamId(streamId);
        _keyFrameRequests[streamId] = true;
        Log.LogInformation("RequestKeyFrame: streamId={StreamId}", streamId);

        // Invalidate GetQualityPreset so the sender's SubscribeToQualityRequests
        // picks up the keyframe request immediately (within one round-trip)
        using (Invalidation.Begin())
            _ = GetQualityPreset(streamId, default);

        return Task.CompletedTask;
    }

    public virtual Task<bool> ConsumeKeyFrameRequest(StreamId streamId, CancellationToken cancellationToken = default)
    {
        ValidateStreamId(streamId);
        return Task.FromResult(_keyFrameRequests.TryRemove(streamId, out _));
    }

    // [ComputeMethod]
    public virtual async Task<VideoQualityPreset> GetQualityPreset(StreamId streamId, CancellationToken cancellationToken)
    {
        if (!_latencyStates.TryGetValue(streamId, out var latencyState))
            return VideoQualityPreset.High;

        // Check if this stream is paused by the priority evaluator (per-stream cached, low chatter)
        if (latencyState.ChatId != default) {
            var isPaused = await LiveVideoBackend.ShouldPause(latencyState.ChatId, streamId, cancellationToken)
                .ConfigureAwait(false);
            if (isPaused)
                return VideoQualityPreset.Paused;
        }

        var preset = await latencyState.QualityPreset.Use(cancellationToken).ConfigureAwait(false);

        // Consume any pending keyframe request (atomic: only first caller gets it)
        if (_keyFrameRequests.TryRemove(streamId, out _))
            preset = preset with { KeyFrameRequested = true };

        return preset;
    }

    // Private methods

    private async IAsyncEnumerable<VideoFrame> PauseAwareFilter(
        StreamId streamId,
        IAsyncEnumerable<VideoFrame> source,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        await foreach (var frame in source.WithCancellation(cancellationToken).ConfigureAwait(false)) {
            var preset = await GetQualityPreset(streamId, cancellationToken).ConfigureAwait(false);
            if (preset.Level != VideoQualityLevel.Paused)
                yield return frame;
            // When paused, frames are silently dropped — viewer sees last decoded frame frozen
        }
    }

    private async IAsyncEnumerable<VideoFrame> TemporalLayerFilter(
        StreamId streamId,
        string peerId,
        IAsyncEnumerable<VideoFrame> source,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        await foreach (var frame in source.WithCancellation(cancellationToken).ConfigureAwait(false)) {
            var latencyState = _latencyStates.GetValueOrDefault(streamId);
            var maxLayer = latencyState?.GetPeerMaxTemporalLayer(peerId) ?? int.MaxValue;
            if (frame.TemporalLayerId <= maxLayer)
                yield return frame;
        }
    }

    private void OnVideoStreamExpire(StreamId streamId)
    {
        _latencyStates.TryRemove(streamId, out _);
        _keyFrameRequests.TryRemove(streamId, out _);
    }

    /// <summary>
    /// Filters video frames to ensure decoder-safe output:
    /// - On startup: skips until the first keyframe (skip-to-live)
    /// - During playback: if a gap is detected (KeyFrameNumber mismatch from dropped frames),
    ///   skips until the next keyframe to avoid feeding broken deltas to the decoder.
    /// </summary>
    internal static async IAsyncEnumerable<VideoFrame> KeyFrameGapFilter(
        IAsyncEnumerable<VideoFrame> source,
        ILogger log,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var lastKeyFrameNumber = -1L;
        var skipping = true; // Start in skip mode — wait for first keyframe
        var skippedCount = 0;

        await foreach (var frame in source.WithCancellation(cancellationToken).ConfigureAwait(false)) {
            if (frame.IsKeyFrame) {
                if (skipping && skippedCount > 0)
                    log.LogInformation(
                        "KeyFrameGapFilter: found keyframe (KF#{KeyFrameNumber}) after skipping {Skipped} frames",
                        frame.KeyFrameNumber, skippedCount);
                lastKeyFrameNumber = frame.KeyFrameNumber;
                skipping = false;
                skippedCount = 0;
                yield return frame;
            }
            else if (!skipping && frame.KeyFrameNumber == lastKeyFrameNumber) {
                yield return frame;
            }
            else {
                // Gap detected or initial skip: non-keyframe with unexpected KeyFrameNumber
                if (!skipping) {
                    skipping = true;
                    log.LogInformation(
                        "KeyFrameGapFilter: gap detected — expected KF#{Expected}, got KF#{Actual}, skipping to next keyframe",
                        lastKeyFrameNumber, frame.KeyFrameNumber);
                }
                skippedCount++;
            }
        }
    }

    private async Task PushVideoInternal(
        VideoRecord record,
        IAsyncEnumerable<VideoFrame> videoFrames,
        CancellationToken cancellationToken)
    {
        var beginsAt = default(Moment) + TimeSpan.FromSeconds(record.ClientStartOffset);
        var rules = await Chats.GetRules(record.Session, record.ChatId, cancellationToken)
            .ConfigureAwait(false);
        rules.Require(ChatPermissions.Write);

        var author = await Authors
            .EnsureJoined(record.Session, record.ChatId, cancellationToken)
            .ConfigureAwait(false);

        // Guard against client clock skew: if clientStartOffset is too far from server time,
        // override with server time to prevent false latency reports and quality step-downs.
        var serverNow = Clocks.ServerClock.Now;
        var clockDelta = serverNow - beginsAt;
        if (Math.Abs(clockDelta.TotalSeconds) > 5) {
            Log.LogWarning("TIMING_ANCHOR: StreamId={StreamId}, client clock skew={ClockDeltaMs:F0}ms, overriding clientStartOffset with server time",
                record.StreamId, clockDelta.TotalMilliseconds);
            beginsAt = serverNow;
        }
        else
            Log.LogInformation("TIMING_ANCHOR: StreamId={StreamId}, ClockDelta={ClockDeltaMs:F0}ms (OK)",
                record.StreamId, clockDelta.TotalMilliseconds);

        // Register stream for real-time signaling
        var streamInfo = new VideoStreamInfo(
            record.StreamId,
            record.ChatId,
            author.Id,
            record.Format,
            beginsAt,
            record.StreamKind);

        // Cross-service RPC call — properly shard-routed via ILiveVideoBackend
        await LiveVideoBackend.Register(record.ChatId, streamInfo, cancellationToken)
            .ConfigureAwait(false);

        _latencyStates[record.StreamId] = new StreamLatencyState(record.ChatId, beginsAt, record.Format, Services.StateFactory(), Log);

        try {
            // Publish video stream for real-time viewing
            // No processing - just forward to StreamStore for memoization
            Log.LogInformation("PushVideoInternal: publishing #{StreamId} to StreamStore", record.StreamId);

            var frameCount = 0;
            var keyFrameNumber = 0L;
            var lastHeartbeat = CpuTimestamp.Now;
            var heartbeatInterval = TimeSpan.FromMinutes(2.5); // Half of LiveVideoBackend.ChatStateTtl
            async IAsyncEnumerable<VideoFrame> ProcessFrames(IAsyncEnumerable<VideoFrame> source)
            {
                await foreach (var frame in source.WithCancellation(cancellationToken).ConfigureAwait(false)) {
                    frameCount++;
                    if (frame.IsKeyFrame)
                        keyFrameNumber++;
                    frame.KeyFrameNumber = keyFrameNumber;

                    // Track throughput for quality adaptation
                    var latencyState = _latencyStates.GetValueOrDefault(record.StreamId);
                    latencyState?.RecordFrameBytes(frame.Data?.Length ?? 0);

                    if (lastHeartbeat.Elapsed >= heartbeatInterval) {
                        lastHeartbeat = CpuTimestamp.Now;
                        // This call is idempotent and just bumps expiration
                        await LiveVideoBackend.Register(record.ChatId, streamInfo, CancellationToken.None)
                            .ConfigureAwait(false);
                    }

                    yield return frame;
                }
            }

            var memoizer = ProcessFrames(videoFrames).Memoize(
                Constants.Video.RetentionBufferSize,
                cancellationToken);
            await _videoStreams.Publish(record.StreamId, memoizer).ConfigureAwait(false);
        }
        finally {
            // Unregister stream when it ends — cross-service RPC call
            await LiveVideoBackend.Unregister(record.ChatId, record.StreamId, CancellationToken.None)
                .ConfigureAwait(false);
            // Latency state cleanup deferred to OnVideoStreamExpire — peers may still read buffered frames
        }
    }

    private void ValidateStreamId(StreamId streamId)
    {
        if (streamId.NodeRef != ThisNode.Ref)
            throw new ArgumentOutOfRangeException(nameof(streamId),
                $"Wrong mesh node: expected {ThisNode.Ref}, but got {streamId.NodeRef}.");
    }

    // Latency state classes

    public sealed class PeerLatencyState
    {
        private readonly Queue<float> _samples = new();
        private readonly Lock _lock = new();
        private readonly CpuTimestamp _createdAt = CpuTimestamp.Now;

        public float MedianLatencyMs { get; private set; }
        public float MedianDecodeTimeMs { get; private set; } = -1;
        public int BufferDepth { get; private set; } = -1;
        public float BufferSpanMs { get; private set; } = -1;
        public int MaxTemporalLayer { get; private set; } = int.MaxValue;
        public bool IsWarmedUp => _createdAt.Elapsed >= Constants.Video.PeerWarmupDuration;

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

    public sealed class StreamLatencyState(ChatId chatId, Moment startedAt, VideoFormat format, StateFactory stateFactory, ILogger log)
    {
        private readonly ILogger Log = log;
        private readonly ILogger? DebugLog = log.IfEnabled(LogLevel.Debug);

        public ChatId ChatId { get; } = chatId;
        public Moment StartedAt { get; } = startedAt;
        public MutableState<VideoQualityPreset> QualityPreset { get; } = stateFactory.NewMutable(VideoQualityPreset.High);

        // Cap max quality to what the camera can actually provide — prevents wasteful upscaling
        private readonly VideoQualityLevel _maxQuality = format switch
        {
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
        private int _consecutiveLowThroughputChecks;


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
        }

        private void EvaluateQuality()
        {
            lock (_evaluationLock) {
                _lastEvaluationAt = CpuTimestamp.Now;

                var currentQuality = QualityPreset.Value.Level;

                // Throughput-based proactive step-down: detect sender upload saturation
                var elapsedSinceCheck = _lastThroughputCheckAt.Elapsed;
                if (elapsedSinceCheck >= Constants.Video.QualityDecisionInterval) {
                    var currentBytes = Interlocked.Read(ref _totalBytesReceived);
                    var bytesDelta = currentBytes - _bytesAtLastCheck;
                    _bytesAtLastCheck = currentBytes;
                    var measuredBps = bytesDelta * 8.0 / elapsedSinceCheck.TotalSeconds;
                    var targetBps = QualityPreset.Value.Bitrate;
                    _lastThroughputCheckAt = CpuTimestamp.Now;

                    if (targetBps > 0 && measuredBps < targetBps * Constants.Video.ThroughputStepDownRatio) {
                        _consecutiveLowThroughputChecks++;
                        if (_consecutiveLowThroughputChecks >= Constants.Video.ThroughputStepDownConsecutiveChecks) {
                            var stepped = VideoQualityPreset.StepDown(currentQuality);
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
                }

                var peers = _peers.ToList();
                if (peers.Count == 0)
                    return;

                // Classify slow peers by root cause:
                // - Network/sender-bound: high latency + low decode time + low buffer → step down helps
                // - Receiver-bound: high latency + high decode time or large buffer → step down won't help
                var networkSlowCount = 0;
                var receiverSlowCount = 0;
                foreach (var (peerId, peer) in peers) {
                    if (peer.MedianLatencyMs <= Constants.Video.HighLatencyThresholdMs)
                        continue;

                    if (peer.IsReceiverBound) {
                        receiverSlowCount++;
                        DebugLog?.LogDebug(
                            "EvaluateQuality: PeerId={PeerId} receiver-bound (decodeMs={DecodeMs:F1}, bufDepth={BufDepth})",
                            peerId, peer.MedianDecodeTimeMs, peer.BufferDepth);
                    }
                    else
                        networkSlowCount++;
                }

                var totalSlowCount = networkSlowCount + receiverSlowCount;
                // Only count network-bound slow peers toward sender quality step-down.
                // Receiver-bound peers are handled via skip-to-live — reducing sender quality
                // would hurt everyone without helping the slow receiver.
                var networkSlowRatio = (float)networkSlowCount / peers.Count;
                var effectiveOutlierRatio = peers.Count <= 3
                    ? Constants.Video.PeerOutlierRatioSmallCall
                    : Constants.Video.PeerOutlierRatio;

                // Step down sender quality only if enough NETWORK-bound peers are slow
                if (networkSlowRatio > effectiveOutlierRatio) {
                    var stepped = VideoQualityPreset.StepDown(currentQuality);
                    if (stepped != null) {
                        _lastQualityChangeAt = CpuTimestamp.Now;
                        QualityPreset.Value = stepped;
                        Log.LogInformation(
                            "EvaluateQuality: STEP DOWN {OldLevel} -> {NewLevel}, networkSlow={NetworkSlow}, receiverSlow={ReceiverSlow}, total={TotalCount} (threshold={Threshold:F2})",
                            currentQuality, stepped.Level, networkSlowCount, receiverSlowCount, peers.Count, effectiveOutlierRatio);
                    }
                }
                // Step up quality if all peers are fast and hysteresis window has elapsed
                else if (totalSlowCount == 0 && _lastQualityChangeAt.Elapsed >= Constants.Video.QualityHysteresisWindow) {
                    var allFast = peers.All(p => p.Value.MedianLatencyMs < Constants.Video.LowLatencyThresholdMs);
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
