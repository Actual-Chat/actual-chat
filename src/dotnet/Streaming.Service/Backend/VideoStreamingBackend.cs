using System.Buffers;
using ActualChat.Diagnostics;
using ActualChat.Streaming.Services;
using ActualChat.Video;
using ActualLab.Diagnostics;
using ActualLab.Rpc;

namespace ActualChat.Streaming;

public class VideoStreamingBackend : IVideoStreamingBackend, IDisposable
{
    // Dedicated pool to avoid SharedArrayPool.Trim() contention under GC pressure
    private static readonly ArrayPool<VideoFrame> VideoFramePool = ArrayPool<VideoFrame>.Create();

    private readonly StreamStore<VideoFrame> _videoStreams;

    private MeshNode ThisNode => field ??= Services.MeshWatcher().ThisNode;
    private IChats Chats => field ??= Services.GetRequiredService<IChats>();
    private IAuthors Authors => field ??= Services.GetRequiredService<IAuthors>();
    private MomentClockSet Clocks => field ??= Services.Clocks();
    private ILiveVideoBackend LiveVideoBackend => field ??= Services.GetRequiredService<ILiveVideoBackend>();
    private StreamLatencyStore LatencyStore => field ??= Services.GetRequiredService<StreamLatencyStore>();
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

        return RpcStream.New(FilteredVideoStream(streamId, peerId, skipTo, stream, cancellationToken));
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

    // Quality control — stream-local state

    // [ComputeMethod]
    public virtual async Task<VideoQualityPreset> GetQualityPreset(StreamId streamId, string peerId, CancellationToken cancellationToken)
    {
        if (!LatencyStore.LatencyStates.TryGetValue(streamId, out var latencyState))
            return VideoQualityPreset.High;

        // Check if this stream is paused by the priority evaluator (cross-service RPC to ChatId shard)
        var isPaused = await LiveVideoBackend.ShouldPause(latencyState.ChatId, streamId, cancellationToken)
            .ConfigureAwait(false);
        if (isPaused)
            return VideoQualityPreset.Paused;

        var preset = await latencyState.QualityPreset.Use(cancellationToken).ConfigureAwait(false);

        // Consume any pending keyframe request (atomic: only first caller gets it)
        if (LatencyStore.KeyFrameRequests.TryRemove(streamId, out _))
            preset = preset with { IsKeyFrameRequested = true };

        // Add per-peer temporal layer filtering
        if (!peerId.IsNullOrEmpty())
            preset = preset with { MaxTemporalLayer = LatencyStore.GetPeerMaxTemporalLayer(streamId, peerId) };

        return preset;
    }

    public virtual Task RequestKeyFrame(StreamId streamId, CancellationToken cancellationToken = default)
    {
        // Rate-limit PLI: collapse multiple receivers' requests into one per cooldown window
        var now = CpuTimestamp.Now;
        var lastTime = LatencyStore.LastKeyFrameRequestTime.GetOrAdd(streamId, now);
        if (lastTime != now && lastTime.Elapsed < Constants.Video.KeyFrameRequestCooldown) {
            Log.LogDebug("RequestKeyFrame: streamId={StreamId} — throttled (last {Elapsed:F1}s ago)",
                streamId, lastTime.Elapsed.TotalSeconds);
            return Task.CompletedTask;
        }
        LatencyStore.LastKeyFrameRequestTime[streamId] = now;
        LatencyStore.KeyFrameRequests[streamId] = true;
        Log.LogInformation("RequestKeyFrame: streamId={StreamId}", streamId);

        // Invalidate GetQualityPreset so computed consumers re-evaluate reactively
        using (Invalidation.Begin())
            _ = GetQualityPreset(streamId, "", default);

        return Task.CompletedTask;
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
        var oldMaxLayer = LatencyStore.GetPeerMaxTemporalLayer(streamId, peerId);
        LatencyStore.ReportPeerLatency(streamId, peerId, streamOffsetMs, medianDecodeTimeMs, bufferDepth, bufferSpanMs);
        var newMaxLayer = LatencyStore.GetPeerMaxTemporalLayer(streamId, peerId);
        if (newMaxLayer != oldMaxLayer)
            using (Invalidation.Begin())
                _ = GetQualityPreset(streamId, peerId, default);
        return Task.CompletedTask;
    }

    // Private methods

    /// <summary>
    /// Combined per-consumer video filter pipeline. Merges temporal layer filtering,
    /// pause-aware filtering, and keyframe gap recovery into a single async iterator
    /// to minimize async state machine overhead (one await-foreach instead of three).
    /// </summary>
    internal async IAsyncEnumerable<VideoFrame> FilteredVideoStream(
        StreamId streamId,
        string peerId,
        TimeSpan skipTo,
        IAsyncEnumerable<VideoFrame> source,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        // --- Quality preset: background task refreshes on invalidation ---
        var preset = VideoQualityPreset.High;
        var refreshCts = cancellationToken.CreateLinkedTokenSource();
        var refreshToken = refreshCts.Token;
        var refreshTask = BackgroundTask.Run(async () => {
            while (!refreshToken.IsCancellationRequested) {
                var computed = await Computed
                    .Capture(() => GetQualityPreset(streamId, peerId, refreshToken), refreshToken)
                    .ConfigureAwait(false);
                preset = computed.Value;
                await computed.WhenInvalidated(refreshToken).ConfigureAwait(false);
            }
        }, refreshToken);

        // --- SkipTo filter state: skip frames until we reach a keyframe at or past skipTo ---
        var skipToActive = skipTo > TimeSpan.Zero;
        var skipToFramesSkipped = 0;

        // --- KeyFrame gap filter state ---
        var lastKeyFrameNumber = -1L;
        var skipping = true; // Start in skip mode — wait for first keyframe
        var skippedCount = 0;

        try {
            await foreach (var frame in source.WithCancellation(cancellationToken).ConfigureAwait(false)) {
                // 0. SkipTo filter — drop all frames before the requested offset (must land on keyframe)
                if (skipToActive) {
                    if (frame.Offset < skipTo || !frame.IsKeyFrame) {
                        skipToFramesSkipped++;
                        continue;
                    }
                    // Found a keyframe at or past skipTo — deactivate skipTo filter
                    if (skipToFramesSkipped > 0)
                        Log.LogInformation(
                            "FilteredVideoStream: SkipTo={SkipTo} — skipped {Skipped} frames, resuming at offset {Offset}",
                            skipTo, skipToFramesSkipped, frame.Offset);
                    skipToActive = false;
                }

                // 1. Temporal layer filter — drop enhancement layers for slow peers
                if (frame.TemporalLayerId > preset.MaxTemporalLayer)
                    continue;

                // 2. Pause filter — drop all frames when stream is paused by priority queue
                if (preset.Level == VideoQualityLevel.Paused)
                    continue;

                // 3. KeyFrame gap filter — ensure decoder-safe output
                if (frame.IsKeyFrame) {
                    if (skipping && skippedCount > 0)
                        Log.LogInformation(
                            "FilteredVideoStream: found keyframe (KF#{KeyFrameNumber}) after skipping {Skipped} frames",
                            frame.KeyFrameNumber, skippedCount);
                    lastKeyFrameNumber = frame.KeyFrameNumber;
                    skipping = false;
                    skippedCount = 0;
                    yield return frame;
                }
                else if (!skipping && frame.KeyFrameNumber == lastKeyFrameNumber)
                    yield return frame;
                else {
                    if (!skipping) {
                        skipping = true;
                        Log.LogInformation(
                            "FilteredVideoStream: gap detected — expected KF#{Expected}, got KF#{Actual}, skipping to next keyframe",
                            lastKeyFrameNumber, frame.KeyFrameNumber);
                    }
                    skippedCount++;
                }
            }
        }
        finally {
            refreshCts.CancelAndDisposeSilently();
            await refreshTask.SuppressCancellationAwait(false);
        }
    }

    private void OnVideoStreamExpire(StreamId streamId)
        => LatencyStore.OnStreamExpire(streamId);

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

        LatencyStore.RegisterStreamLatencyState(record.StreamId, record.ChatId, beginsAt, record.Format);

        try {
            // Publish video stream for real-time viewing
            // No processing - just forward to StreamStore for memoization
            Log.LogInformation("PushVideoInternal: publishing #{StreamId} to StreamStore", record.StreamId);

            var keyFrameNumber = 0L;
            var lastHeartbeat = CpuTimestamp.Now;
            var heartbeatInterval = TimeSpan.FromMinutes(2.5); // Half of LiveVideoBackend.ChatStateTtl
            async IAsyncEnumerable<VideoFrame> ProcessFrames(IAsyncEnumerable<VideoFrame> source)
            {
                await foreach (var frame in source.WithCancellation(cancellationToken).ConfigureAwait(false)) {
                    if (frame.IsKeyFrame)
                        keyFrameNumber++;
                    frame.KeyFrameNumber = keyFrameNumber;

                    // Track throughput for quality adaptation (same node, direct call)
                    LatencyStore.RecordFrameBytes(record.StreamId, frame.CachedSerializedBytes?.Length ?? frame.Data?.Length ?? 0);

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
                VideoFramePool,
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
}
