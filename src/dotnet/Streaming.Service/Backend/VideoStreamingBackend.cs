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

        Log.LogInformation("GetVideo: #{StreamId} found, wrapping with logging", streamId);

        // Debug: wrap stream to count and log frames being sent to client
        var frameCount = 0;
        async IAsyncEnumerable<VideoFrame> LogFrames(IAsyncEnumerable<VideoFrame> source)
        {
            await foreach (var frame in source.WithCancellation(cancellationToken).ConfigureAwait(false)) {
                frameCount++;
                if (frameCount <= 3 || frameCount % 100 == 0)
                    DebugLog?.LogDebug("GetVideo sending frame #{Count}: Offset={Offset}ms, Size={Size}, IsKey={IsKey}, PeerId={PeerId}",
                        frameCount, frame.Offset.TotalMilliseconds, frame.Data?.Length ?? 0, frame.IsKeyFrame, peerId);
                yield return frame;
            }
            DebugLog?.LogDebug("GetVideo stream ended after {Count} frames for PeerId={PeerId}", frameCount, peerId);
        }

        // Always start from the next live keyframe — skip all buffered frames
        // and wait for a fresh keyframe at the live edge for near-zero latency.
        stream = SkipToNextKeyFrame(LogFrames(stream), Log, cancellationToken);

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
                Log.LogWarning("ReportPeerLatency: #{StreamId}, PeerId={PeerId}, StreamOffsetMs={StreamOffsetMs:F0}, LatencyMs={LatencyMs:F0}, DecodeMs={DecodeMs:F1}, BufDepth={BufDepth}, BufSpanMs={BufSpanMs:F0}",
                    streamId, peerId, streamOffsetMs, latency.TotalMilliseconds, medianDecodeTimeMs, bufferDepth, bufferSpanMs);
                latencyState.RecordPeerLatency(peerId, (float)latency.TotalMilliseconds,
                    (float)medianDecodeTimeMs, bufferDepth, (float)bufferSpanMs);
            }
            else {
                Log.LogWarning("ReportPeerLatency: #{StreamId}, PeerId={PeerId}, negative latency={LatencyMs:F0}ms (clock skew?), skipping",
                    streamId, peerId, latency.TotalMilliseconds);
            }
            return Task.CompletedTask;
        }
        Log.LogWarning("ReportPeerLatency: No latency state for stream #{StreamId}", streamId);
        return Task.CompletedTask;
    }

    // [ComputeMethod]
    public virtual async Task<VideoQualityPreset> GetQualityPreset(StreamId streamId, CancellationToken cancellationToken)
    {
        if (_latencyStates.TryGetValue(streamId, out var latencyState))
            return await latencyState.QualityPreset.Use(cancellationToken).ConfigureAwait(false);

        return VideoQualityPreset.High;
    }

    // Private methods

    private void OnVideoStreamExpire(StreamId streamId)
        => _latencyStates.TryRemove(streamId, out _);

    /// <summary>
    /// Skip-to-live: discard all buffered frames, then wait for the next live keyframe.
    /// Used when the client re-requests the stream after detecting high latency.
    /// Waiting for the next keyframe naturally eliminates latency — the keyframe is produced
    /// at the live edge, so the consumer starts with near-zero delay.
    /// </summary>
    private static async IAsyncEnumerable<VideoFrame> SkipToNextKeyFrame(
        IAsyncEnumerable<VideoFrame> stream,
        ILogger log,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var enumerator = stream.GetAsyncEnumerator(cancellationToken);
        await using var _ = enumerator.ConfigureAwait(false);
        var skipped = 0;

        // Phase 1: Skip all synchronously-available (buffered) frames
        while (true) {
            var moveNext = enumerator.MoveNextAsync();
            if (!moveNext.IsCompleted) {
                // Reached live edge — await this frame
                if (!await moveNext.ConfigureAwait(false))
                    yield break;
                break;
            }
            if (!moveNext.Result)
                yield break; // stream ended
            skipped++;
        }

        // Phase 2: At the live edge — skip delta frames until next keyframe
        // The current frame (from Phase 1 break) might be a keyframe
        if (enumerator.Current.IsKeyFrame) {
            log.LogInformation(
                "SkipToNextKeyFrame: found keyframe at offset {Offset}ms after skipping {Skipped} buffered frames",
                enumerator.Current.Offset.TotalMilliseconds, skipped);
            yield return enumerator.Current;
        }
        else {
            skipped++;
            // Wait for the next keyframe (at most ~1s at 30fps/GOP=30)
            while (await enumerator.MoveNextAsync().ConfigureAwait(false)) {
                if (enumerator.Current.IsKeyFrame) {
                    log.LogInformation(
                        "SkipToNextKeyFrame: found keyframe at offset {Offset}ms after skipping {Skipped} frames",
                        enumerator.Current.Offset.TotalMilliseconds, skipped);
                    yield return enumerator.Current;
                    break;
                }
                skipped++;
            }
        }

        // Phase 3: Pass-through remaining frames
        while (await enumerator.MoveNextAsync().ConfigureAwait(false))
            yield return enumerator.Current;
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
            beginsAt);

        // Cross-service RPC call — properly shard-routed via ILiveVideoBackend
        await LiveVideoBackend.Register(record.ChatId, streamInfo, cancellationToken)
            .ConfigureAwait(false);

        _latencyStates[record.StreamId] = new StreamLatencyState(beginsAt, record.Format, Services.StateFactory(), Log);

        try {
            // Publish video stream for real-time viewing
            // No processing - just forward to StreamStore for memoization
            Log.LogInformation("PushVideoInternal: publishing #{StreamId} to StreamStore", record.StreamId);

            // TODO(AK): Call LiveVideoBackend.Register again to maintain (bump) expiring state once per Half of LiveVideoBackend.ChatStateTtl
            var frameCount = 0;
            async IAsyncEnumerable<VideoFrame> LogFrames(IAsyncEnumerable<VideoFrame> source)
            {
                await foreach (var frame in source.WithCancellation(cancellationToken).ConfigureAwait(false)) {
                    frameCount++;
                    if (frameCount <= 3 || frameCount % 100 == 0)
                        DebugLog?.LogDebug(
                            "PushVideoInternal frame #{Count}: Offset={Offset}ms, IsKey={IsKey}, DataLen={DataLen}, DescLen={DescLen}",
                            frameCount, frame.Offset.TotalMilliseconds, frame.IsKeyFrame, frame.Data?.Length ?? 0, frame.Description?.Length ?? 0);
                    yield return frame;
                }
                DebugLog?.LogDebug("PushVideoInternal: stream completed with {Count} frames", frameCount);
            }

            var memoizer = LogFrames(videoFrames).Memoize(
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
            }
        }
    }

    public sealed class StreamLatencyState(Moment startedAt, VideoFormat format, StateFactory stateFactory, ILogger log)
    {
        private readonly ILogger Log = log;
        private readonly ILogger? DebugLog = log.IfEnabled(LogLevel.Debug);

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

        private void EvaluateQuality()
        {
            lock (_evaluationLock) {
                _lastEvaluationAt = CpuTimestamp.Now;

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
                var currentQuality = QualityPreset.Value.Level;
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
