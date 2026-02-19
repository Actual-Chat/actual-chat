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
            await foreach (var frame in source.WithCancellation(cancellationToken)) {
                frameCount++;
                if (frameCount <= 3 || frameCount % 100 == 0)
                    Log.LogDebug("GetVideo sending frame #{Count}: Offset={Offset}ms, Size={Size}, IsKey={IsKey}, PeerId={PeerId}",
                        frameCount, frame.Offset.TotalMilliseconds, frame.Data?.Length ?? 0, frame.IsKeyFrame, peerId);
                yield return frame;
            }
            Log.LogDebug("GetVideo stream ended after {Count} frames for PeerId={PeerId}", frameCount, peerId);
        }

        stream = SkipToKeyFrame(LogFrames(stream), skipTo, cancellationToken);

        stream = ApplyGopSkipping(stream, streamId, peerId, cancellationToken);

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

    public virtual Task ReportPeerLatency(StreamId streamId, string peerId, double streamOffsetMs, CancellationToken cancellationToken = default)
    {
        if (_latencyStates.TryGetValue(streamId, out var latencyState)) {
            var latency = Clocks.ServerClock.Now - (latencyState.StartedAt + TimeSpan.FromMilliseconds(streamOffsetMs));
            if (latency > TimeSpan.Zero) {
                AppMeters.VideoLatency.Record((float)latency.TotalMilliseconds);
                Log.LogWarning("ReportPeerLatency: StreamId={StreamId}, PeerId={PeerId}, StreamOffsetMs={StreamOffsetMs:F0}, LatencyMs={LatencyMs:F0}",
                    streamId, peerId, streamOffsetMs, latency.TotalMilliseconds);
                latencyState.RecordPeerLatency(peerId, (float)latency.TotalMilliseconds);
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
            return Task.FromResult(RpcStream.New(directives, isReconnectable: false));
        }

        // Stream not found — return a stream with just the default quality
        Log.LogWarning("ObserveStreamQualityRequests: StreamId={StreamId} not found, returning default High quality", streamId);
        async IAsyncEnumerable<VideoQualityPreset> DefaultStream()
        {
            yield return VideoQualityPreset.High;
            await Task.Delay(System.Threading.Timeout.InfiniteTimeSpan, cancellationToken).ConfigureAwait(false);
        }
        return Task.FromResult(RpcStream.New(DefaultStream(), isReconnectable: false));
    }

    // Private methods

    private void OnVideoStreamExpire(StreamId streamId)
    {
        if (_latencyStates.TryRemove(streamId, out var ls))
            ls.Complete();
    }

    private bool ShouldSkipGopsForPeer(StreamId streamId, string peerId)
    {
        if (!_latencyStates.TryGetValue(streamId, out var latencyState))
            return false;
        return latencyState.ShouldSkipGopsForPeer(peerId);
    }

    private async IAsyncEnumerable<VideoFrame> ApplyGopSkipping(
        IAsyncEnumerable<VideoFrame> source,
        StreamId streamId,
        string peerId,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var skippingGop = false;
        var gopCount = 0;
        var skippedGopCount = 0;
        await foreach (var frame in source.WithCancellation(cancellationToken)) {
            if (frame.IsKeyFrame) {
                var wasSkipping = skippingGop;
                // Re-evaluate at each GOP boundary
                skippingGop = ShouldSkipGopsForPeer(streamId, peerId);
                gopCount++;
                if (skippingGop)
                    skippedGopCount++;

                if (wasSkipping != skippingGop)
                    Log.LogInformation("ApplyGopSkipping: {Action} skipping GOPs for PeerId={PeerId}, StreamId={StreamId}",
                        skippingGop ? "START" : "STOP", peerId, streamId);

                if (gopCount % 100 == 0)
                    Log.LogDebug("ApplyGopSkipping: PeerId={PeerId}, StreamId={StreamId}, GOPs={GopCount}, Skipped={SkippedCount}",
                        peerId, streamId, gopCount, skippedGopCount);
            }
            if (!skippingGop)
                yield return frame;
        }
    }

    private async Task PushVideoInternal(
        VideoRecord record,
        IAsyncEnumerable<VideoFrame> videoFrames,
        CancellationToken cancellationToken)
    {
        var beginsAt = Clocks.SystemClock.Now;
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

        _latencyStates[record.StreamId] = new StreamLatencyState(Log, beginsAt);

        try {
            // Publish video stream for real-time viewing
            // No processing - just forward to StreamStore for memoization
            Log.LogInformation("PushVideoInternal: Publishing stream {StreamId} to StreamStore", record.StreamId);

            var frameCount = 0;
            async IAsyncEnumerable<VideoFrame> LogFrames(IAsyncEnumerable<VideoFrame> source)
            {
                await foreach (var frame in source.WithCancellation(cancellationToken)) {
                    frameCount++;
                    if (frameCount <= 3 || frameCount % 100 == 0)
                        Log.LogDebug("PushVideoInternal frame #{Count}: Offset={Offset}ms, IsKey={IsKey}, DataLen={DataLen}, DescLen={DescLen}",
                            frameCount, frame.Offset.TotalMilliseconds, frame.IsKeyFrame, frame.Data?.Length ?? 0, frame.Description?.Length ?? 0);
                    yield return frame;
                }
                Log.LogDebug("PushVideoInternal: stream completed with {Count} frames", frameCount);
            }

            var memoizer = LogFrames(videoFrames).SlidingMemoize(
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

    private static IAsyncEnumerable<VideoFrame> SkipToKeyFrame(
        IAsyncEnumerable<VideoFrame> stream,
        TimeSpan skipTo,
        CancellationToken cancellationToken)
    {
        _ = cancellationToken; // Reserved for future use
        if (skipTo <= TimeSpan.Zero)
            return stream;

        // Skip frames until we find a keyframe at or after the requested position.
        // For video, we must start from a keyframe to decode correctly.
        return stream.SkipWhile(frame => frame.Offset < skipTo || !frame.IsKeyFrame);
    }

    // Latency state classes

    public sealed class PeerLatencyState
    {
        private readonly Queue<float> _samples = new();
        private readonly Lock _lock = new();
        private int _gopCounter;

        public float MedianLatencyMs { get; private set; }
        public int GopSkipRatio { get; set; } // 0=none, 1=skip every other GOP, 2=skip 2 of 3

        public void RecordLatency(float latencyMs)
        {
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
            }
        }

        public bool ShouldSkipNextGop()
        {
            if (GopSkipRatio <= 0)
                return false;

            var counter = Interlocked.Increment(ref _gopCounter);
            // ratio=1 → skip every other (skip when counter%2==0)
            // ratio=2 → skip 2 of 3 (skip when counter%3!=0)
            return GopSkipRatio switch {
                1 => counter % 2 == 0,
                2 => counter % 3 != 0,
                _ => false,
            };
        }
    }

    public sealed class StreamLatencyState(ILogger log, Moment startedAt)
    {
        public Moment StartedAt { get; } = startedAt;

        private readonly ConcurrentDictionary<string, PeerLatencyState> _peers = new(StringComparer.Ordinal);
        private readonly AsyncObservable<VideoQualityPreset> _qualityDirectives = new();
        private readonly Lock _evaluationLock = new();

        private VideoQualityLevel _currentQuality = VideoQualityLevel.High;
        private CpuTimestamp _lastQualityChangeAt = CpuTimestamp.Now;
        private CpuTimestamp _lastEvaluationAt;

        public VideoQualityLevel CurrentQuality => _currentQuality;

        public void RecordPeerLatency(string peerId, float latencyMs)
        {
            var peer = _peers.GetOrAdd(peerId, _ => new PeerLatencyState());
            peer.RecordLatency(latencyMs);
            log.LogDebug("RecordPeerLatency: PeerId={PeerId}, LatencyMs={LatencyMs:F0}, MedianMs={MedianMs:F0}",
                peerId, latencyMs, peer.MedianLatencyMs);

            // Throttle evaluation to QualityDecisionInterval
            if (_lastEvaluationAt.Elapsed >= Constants.Video.QualityDecisionInterval)
                EvaluateQuality();
        }

        public bool ShouldSkipGopsForPeer(string peerId)
        {
            if (!_peers.TryGetValue(peerId, out var peer))
                return false;
            return peer.ShouldSkipNextGop();
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

                var slowCount = peers.Count(p => p.Value.MedianLatencyMs > Constants.Video.HighLatencyThresholdMs);
                var slowRatio = (float)slowCount / peers.Count;

                // Step down sender quality if majority are slow
                if (slowRatio > Constants.Video.PeerOutlierRatio) {
                    var stepped = VideoQualityPreset.StepDown(_currentQuality);
                    if (stepped != null) {
                        var oldQuality = _currentQuality;
                        _currentQuality = stepped.Level;
                        _lastQualityChangeAt = CpuTimestamp.Now;
                        _qualityDirectives.Publish(stepped);
                        log.LogInformation("EvaluateQuality: STEP DOWN {OldLevel} -> {NewLevel}, slowRatio={SlowRatio:F2} ({SlowCount}/{TotalCount})",
                            oldQuality, stepped.Level, slowRatio, slowCount, peers.Count);
                    }
                }
                // Step up quality if all peers are fast and hysteresis window has elapsed
                else if (slowCount == 0
                    && _lastQualityChangeAt.Elapsed >= Constants.Video.QualityHysteresisWindow) {
                    var allFast = peers.All(p => p.Value.MedianLatencyMs < Constants.Video.LowLatencyThresholdMs);
                    if (allFast) {
                        var stepped = VideoQualityPreset.StepUp(_currentQuality);
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
                    log.LogDebug("EvaluateQuality: HOLD at {Level}, slowRatio={SlowRatio:F2} ({SlowCount}/{TotalCount})",
                        _currentQuality, slowRatio, slowCount, peers.Count);
                }

                // Per-peer GOP skipping for individual outliers
                foreach (var (peerId, peer) in peers)
                    if (peer.MedianLatencyMs > Constants.Video.GopSkipThresholdMs) {
                        if (peer.GopSkipRatio == 0) {
                            peer.GopSkipRatio = 1;
                            log.LogInformation("EvaluateQuality: Enable GOP skipping for PeerId={PeerId}, MedianMs={MedianMs:F0}, ratio=1",
                                peerId, peer.MedianLatencyMs);
                        }
                    }
                    else if (peer.MedianLatencyMs < Constants.Video.GopSkipRecoveryMs)
                        if (peer.GopSkipRatio > 0) {
                            peer.GopSkipRatio = 0;
                            log.LogInformation("EvaluateQuality: Disable GOP skipping for PeerId={PeerId}, MedianMs={MedianMs:F0}",
                                peerId, peer.MedianLatencyMs);
                        }
            }
        }

        public void Complete(Exception? error = null)
            => _qualityDirectives.TryComplete(error);
    }
}
