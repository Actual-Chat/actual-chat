using ActualChat.Chat;
using ActualChat.Diagnostics;
using ActualChat.Streaming.Services;
using ActualChat.Video;
using ActualLab.Rpc;

namespace ActualChat.Streaming;

public class VideoStreamingBackend : IVideoStreamingBackend, IDisposable
{
    private readonly StreamStore<VideoFrame> _videoStreams;
    private readonly ConcurrentDictionary<StreamId, StreamLatencyState> _latencyStates = new();

    private ILogger Log => field ??= Services.LogFor(GetType());
    private MeshNode ThisNode => field ??= Services.MeshWatcher().ThisNode;
    private IChats Chats => field ??= Services.GetRequiredService<IChats>();
    private IAuthors Authors => field ??= Services.GetRequiredService<IAuthors>();
    private MomentClockSet Clocks => field ??= Services.Clocks();
    private ILiveVideoBackend LiveVideoBackend => field ??= Services.GetRequiredService<ILiveVideoBackend>();

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
        Log.LogInformation("GetVideo: StreamId={StreamId}, SkipTo={SkipTo}, PeerId={PeerId}", streamId, skipTo, peerId);
        var stream = await _videoStreams.Get(streamId, cancellationToken).ConfigureAwait(false);
        if (stream == null) {
            Log.LogWarning("GetVideo: Stream {StreamId} not found in StreamStore", streamId);
            return null;
        }

        Log.LogInformation("GetVideo: Stream {StreamId} found, wrapping with logging", streamId);

        // Debug: wrap stream to count and log frames being sent to client
        var frameCount = 0;
        async IAsyncEnumerable<VideoFrame> LogFrames(IAsyncEnumerable<VideoFrame> source)
        {
            await foreach (var frame in source.WithCancellation(cancellationToken).ConfigureAwait(false)) {
                frameCount++;
                if (frameCount <= 3 || frameCount % 100 == 0)
                    Log.LogDebug("GetVideo sending frame #{Count}: Offset={Offset}ms, Size={Size}, IsKey={IsKey}, PeerId={PeerId}",
                        frameCount, frame.Offset.TotalMilliseconds, frame.Data?.Length ?? 0, frame.IsKeyFrame, peerId);
                yield return frame;
            }
            Log.LogDebug("GetVideo stream ended after {Count} frames for PeerId={PeerId}", frameCount, peerId);
        }

        // Always skip to the latest buffered keyframe to minimize stale frame delivery.
        // skipTo is typically ~0ms (viewer discovers stream nearly simultaneously with registration),
        // so SkipToKeyFrame would replay the entire buffer. SkipToLatestBufferedKeyFrame jumps to
        // the most recent keyframe in the memoizer's replay buffer for efficient near-live start.
        stream = SkipToLatestBufferedKeyFrame(LogFrames(stream), Log, cancellationToken);

        stream = ApplySkipToLive(stream, streamId, peerId, cancellationToken);

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
            Log.LogError(e, "Error pushing video stream {StreamId}", record.StreamId);
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
                Log.LogWarning("ReportPeerLatency: StreamId={StreamId}, PeerId={PeerId}, StreamOffsetMs={StreamOffsetMs:F0}, LatencyMs={LatencyMs:F0}, DecodeMs={DecodeMs:F1}, BufDepth={BufDepth}, BufSpanMs={BufSpanMs:F0}",
                    streamId, peerId, streamOffsetMs, latency.TotalMilliseconds, medianDecodeTimeMs, bufferDepth, bufferSpanMs);
                latencyState.RecordPeerLatency(peerId, (float)latency.TotalMilliseconds,
                    (float)medianDecodeTimeMs, bufferDepth, (float)bufferSpanMs);
            }
            else {
                Log.LogWarning("ReportPeerLatency: StreamId={StreamId}, PeerId={PeerId}, negative latency={LatencyMs:F0}ms (clock skew?), skipping",
                    streamId, peerId, latency.TotalMilliseconds);
            }
            return Task.CompletedTask;
        }
        Log.LogWarning("ReportPeerLatency: No latency state for StreamId={StreamId}", streamId);
        return Task.CompletedTask;
    }

    public virtual Task<RpcStream<VideoQualityPreset>> ObserveStreamQualityRequests(
        StreamId streamId,
        CancellationToken cancellationToken)
    {
        if (_latencyStates.TryGetValue(streamId, out var latencyState)) {
            Log.LogInformation("ObserveStreamQualityRequests: StreamId={StreamId} found", streamId);
            var directives = latencyState.ObserveQualityDirectives(cancellationToken);
            return Task.FromResult(RpcStream.New(directives, allowReconnect: false));
        }

        // Stream not found — return a stream with just the default quality
        Log.LogWarning("ObserveStreamQualityRequests: StreamId={StreamId} not found, returning default High quality", streamId);
        async IAsyncEnumerable<VideoQualityPreset> DefaultStream()
        {
            yield return VideoQualityPreset.High;
            await Task.Delay(System.Threading.Timeout.InfiniteTimeSpan, cancellationToken).ConfigureAwait(false);
        }
        return Task.FromResult(RpcStream.New(DefaultStream(), allowReconnect: false));
    }

    // Private methods

    private void OnVideoStreamExpire(StreamId streamId)
    {
        if (_latencyStates.TryRemove(streamId, out var ls))
            ls.Complete();
    }

    private bool ShouldSkipToLive(StreamId streamId, string peerId)
    {
        if (!_latencyStates.TryGetValue(streamId, out var latencyState))
            return false;
        return latencyState.ShouldSkipToLive(peerId);
    }

    private void ClearSkipToLive(StreamId streamId, string peerId)
    {
        if (_latencyStates.TryGetValue(streamId, out var latencyState))
            latencyState.ClearSkipToLive(peerId);
    }

    private async IAsyncEnumerable<VideoFrame> ApplySkipToLive(
        IAsyncEnumerable<VideoFrame> source,
        StreamId streamId,
        string peerId,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var enumerator = source.GetAsyncEnumerator(cancellationToken);
        await using var _ = enumerator.ConfigureAwait(false);

        while (true) {
            // Check skip-to-live flag before awaiting next frame
            if (ShouldSkipToLive(streamId, peerId)) {
                Log.LogInformation("ApplySkipToLive: START for PeerId={PeerId}, StreamId={StreamId}",
                    peerId, streamId);
                VideoFrame? lastKeyFrame = null;
                var skippedCount = 0;

                // Consume all synchronously-available (buffered) frames
                while (true) {
                    var moveNext = enumerator.MoveNextAsync();
                    if (!moveNext.IsCompleted) {
                        // Reached live edge
                        ClearSkipToLive(streamId, peerId);
                        if (lastKeyFrame != null) {
                            Log.LogInformation(
                                "ApplySkipToLive: DONE for PeerId={PeerId}, skipped {Skipped} frames, resuming from keyframe at {Offset}ms",
                                peerId, skippedCount, lastKeyFrame.Offset.TotalMilliseconds);
                            yield return lastKeyFrame;
                        } else
                            Log.LogInformation(
                                "ApplySkipToLive: DONE for PeerId={PeerId}, no keyframe found in {Skipped} buffered frames, continuing",
                                peerId, skippedCount);
                        // Await the pending live frame
                        if (!await moveNext.ConfigureAwait(false))
                            yield break;
                        yield return enumerator.Current;
                        break; // back to outer while(true) loop
                    }
                    if (!moveNext.Result)
                        yield break; // stream ended

                    var frame = enumerator.Current;
                    if (frame.IsKeyFrame)
                        lastKeyFrame = frame;
                    skippedCount++;
                }
            }
            else {
                // Normal pass-through
                if (!await enumerator.MoveNextAsync().ConfigureAwait(false))
                    yield break;
                yield return enumerator.Current;
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

        // Register stream for real-time signaling
        var streamInfo = new VideoStreamInfo(
            record.StreamId,
            record.ChatId,
            author.Id,
            record.Format,
            beginsAt);

        // Cross-service RPC call — properly shard-routed via ILiveVideoBackend
        await LiveVideoBackend.RegisterActiveStream(record.ChatId, streamInfo, cancellationToken)
            .ConfigureAwait(false);

        _latencyStates[record.StreamId] = new StreamLatencyState(Log, beginsAt, record.Format);

        try {
            // Publish video stream for real-time viewing
            // No processing - just forward to StreamStore for memoization
            Log.LogInformation("PushVideoInternal: Publishing stream {StreamId} to StreamStore", record.StreamId);

            var frameCount = 0;
            async IAsyncEnumerable<VideoFrame> LogFrames(IAsyncEnumerable<VideoFrame> source)
            {
                await foreach (var frame in source.WithCancellation(cancellationToken).ConfigureAwait(false)) {
                    frameCount++;
                    if (frameCount <= 3 || frameCount % 100 == 0)
                        Log.LogDebug("PushVideoInternal frame #{Count}: Offset={Offset}ms, IsKey={IsKey}, DataLen={DataLen}, DescLen={DescLen}",
                            frameCount, frame.Offset.TotalMilliseconds, frame.IsKeyFrame, frame.Data?.Length ?? 0, frame.Description?.Length ?? 0);
                    yield return frame;
                }
                Log.LogDebug("PushVideoInternal: stream completed with {Count} frames", frameCount);
            }

            var memoizer = LogFrames(videoFrames).Memoize(
                Constants.Video.RetentionBufferSize,
                cancellationToken);
            await _videoStreams.Publish(record.StreamId, memoizer).ConfigureAwait(false);
        }
        finally {
            // Unregister stream when it ends — cross-service RPC call
            await LiveVideoBackend.UnregisterActiveStream(record.ChatId, record.StreamId, CancellationToken.None)
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

    /// <summary>
    /// Skips to the latest keyframe in the memoizer's replay buffer.
    /// Buffered frames are detected by checking MoveNextAsync().IsCompleted —
    /// AsyncMemoizer.Replay() pre-fills a channel synchronously, so buffered
    /// reads complete instantly while live reads are async.
    /// </summary>
    private static async IAsyncEnumerable<VideoFrame> SkipToLatestBufferedKeyFrame(
        IAsyncEnumerable<VideoFrame> stream,
        ILogger log,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var buffer = new List<VideoFrame>();
        var lastKeyFrameIdx = -1;

        var enumerator = stream.GetAsyncEnumerator(cancellationToken);
        await using var _ = enumerator.ConfigureAwait(false);

        // Phase 1: Read all synchronously-available (buffered) frames
        while (true) {
            var moveNext = enumerator.MoveNextAsync();

            if (!moveNext.IsCompleted) {
                // Buffer exhausted — moveNext is the first live frame (still pending)
                var startIdx = lastKeyFrameIdx >= 0 ? lastKeyFrameIdx : 0;
                if (startIdx > 0)
                    log.LogInformation(
                        "SkipToLatestBufferedKeyFrame: skipped {Skipped} frames, emitting {Emitted} from buffer (total={Total}, lastKeyFrame at #{Idx})",
                        startIdx, buffer.Count - startIdx, buffer.Count, lastKeyFrameIdx);

                for (var i = startIdx; i < buffer.Count; i++)
                    yield return buffer[i];
                buffer.Clear();

                // Await the pending live frame
                if (!await moveNext.ConfigureAwait(false))
                    yield break;
                yield return enumerator.Current;
                break;
            }

            if (!moveNext.Result) {
                // Stream ended during buffer read
                var startIdx = lastKeyFrameIdx >= 0 ? lastKeyFrameIdx : 0;
                for (var i = startIdx; i < buffer.Count; i++)
                    yield return buffer[i];
                yield break;
            }

            var frame = enumerator.Current;
            buffer.Add(frame);
            if (frame.IsKeyFrame)
                lastKeyFrameIdx = buffer.Count - 1;
        }

        // Phase 2: Pass-through for remaining live frames
        while (await enumerator.MoveNextAsync().ConfigureAwait(false))
            yield return enumerator.Current;
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
        public volatile bool SkipToLive;

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

    public sealed class StreamLatencyState(ILogger log, Moment startedAt, VideoFormat format)
    {
        public Moment StartedAt { get; } = startedAt;

        // Cap max quality to what the camera can actually provide — prevents wasteful upscaling
        private readonly VideoQualityLevel _maxQuality = format.Width >= 1920 && format.Height >= 1080
            ? VideoQualityLevel.Full
            : format is { Width: >= 1280, Height: >= 720 }
                ? VideoQualityLevel.High
                : format is { Width: >= 960, Height: >= 540 }
                    ? VideoQualityLevel.Medium
                    : VideoQualityLevel.Low;

        private readonly ConcurrentDictionary<string, PeerLatencyState> _peers = new();
        private readonly AsyncObservable<VideoQualityPreset> _qualityDirectives = new();
        private readonly Lock _evaluationLock = new();

        private VideoQualityLevel _currentQuality = VideoQualityLevel.High;
        private CpuTimestamp _lastQualityChangeAt = CpuTimestamp.Now;
        private CpuTimestamp _lastEvaluationAt;

        public VideoQualityLevel CurrentQuality => _currentQuality;

        public void RecordPeerLatency(string peerId, float latencyMs,
            float medianDecodeTimeMs = -1, int bufferDepth = -1, float bufferSpanMs = -1)
        {
            var peer = _peers.GetOrAdd(peerId, _ => new PeerLatencyState());
            peer.RecordLatency(latencyMs, medianDecodeTimeMs, bufferDepth, bufferSpanMs);
            log.LogDebug("RecordPeerLatency: PeerId={PeerId}, LatencyMs={LatencyMs:F0}, MedianMs={MedianMs:F0}, ReceiverBound={ReceiverBound}",
                peerId, latencyMs, peer.MedianLatencyMs, peer.IsReceiverBound);

            // Skip-to-live trigger: if raw latency exceeds threshold and peer is warmed up
            if (latencyMs > Constants.Video.SkipToLiveThresholdMs
                && peer.IsWarmedUp
                && !peer.SkipToLive) {
                peer.SkipToLive = true;
                log.LogInformation(
                    "RecordPeerLatency: SkipToLive triggered for PeerId={PeerId}, LatencyMs={LatencyMs:F0}",
                    peerId, latencyMs);
            }

            // Throttle evaluation to QualityDecisionInterval
            if (_lastEvaluationAt.Elapsed >= Constants.Video.QualityDecisionInterval)
                EvaluateQuality();
        }

        public bool ShouldSkipToLive(string peerId)
            => _peers.TryGetValue(peerId, out var peer) && peer.SkipToLive;

        public void ClearSkipToLive(string peerId)
        {
            if (_peers.TryGetValue(peerId, out var peer))
                peer.SkipToLive = false;
        }

        public async IAsyncEnumerable<VideoQualityPreset> ObserveQualityDirectives(
            [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            var subscription = _qualityDirectives.Subscribe();
            await using var _ = subscription.ConfigureAwait(false);

            // Emit current quality as the first directive
            yield return VideoQualityPreset.ForLevel(_currentQuality);

            await foreach (var preset in subscription.Reader.ReadAllAsync(cancellationToken).ConfigureAwait(false))
                yield return preset;
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
                        log.LogDebug("EvaluateQuality: PeerId={PeerId} receiver-bound (decodeMs={DecodeMs:F1}, bufDepth={BufDepth})",
                            peerId, peer.MedianDecodeTimeMs, peer.BufferDepth);
                    }
                    else {
                        networkSlowCount++;
                    }
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
                    var stepped = VideoQualityPreset.StepDown(_currentQuality);
                    if (stepped != null) {
                        var oldQuality = _currentQuality;
                        _currentQuality = stepped.Level;
                        _lastQualityChangeAt = CpuTimestamp.Now;
                        _qualityDirectives.Publish(stepped);
                        log.LogInformation("EvaluateQuality: STEP DOWN {OldLevel} -> {NewLevel}, networkSlow={NetworkSlow}, receiverSlow={ReceiverSlow}, total={TotalCount} (threshold={Threshold:F2})",
                            oldQuality, stepped.Level, networkSlowCount, receiverSlowCount, peers.Count, effectiveOutlierRatio);
                    }
                }
                // Step up quality if all peers are fast and hysteresis window has elapsed
                else if (totalSlowCount == 0
                    && _lastQualityChangeAt.Elapsed >= Constants.Video.QualityHysteresisWindow) {
                    var allFast = peers.All(p => p.Value.MedianLatencyMs < Constants.Video.LowLatencyThresholdMs);
                    if (allFast) {
                        var stepped = VideoQualityPreset.StepUp(_currentQuality);
                        if (stepped != null && stepped.Level < _maxQuality) {
                            log.LogInformation("EvaluateQuality: SKIP step-up to {Level}, camera max is {MaxLevel}",
                                stepped.Level, _maxQuality);
                            stepped = null;
                        }
                        if (stepped != null) {
                            var oldQuality = _currentQuality;
                            _currentQuality = stepped.Level;
                            _lastQualityChangeAt = CpuTimestamp.Now;
                            _qualityDirectives.Publish(stepped);
                            log.LogInformation("EvaluateQuality: STEP UP {OldLevel} -> {NewLevel}, all peers fast ({TotalCount} peers)",
                                oldQuality, stepped.Level, peers.Count);
                        }
                    }
                }
                else {
                    log.LogDebug("EvaluateQuality: HOLD at {Level}, networkSlow={NetworkSlow}, receiverSlow={ReceiverSlow}, total={TotalCount}",
                        _currentQuality, networkSlowCount, receiverSlowCount, peers.Count);
                }
            }
        }

        public void Complete(Exception? error = null)
            => _qualityDirectives.TryComplete(error);
    }
}
