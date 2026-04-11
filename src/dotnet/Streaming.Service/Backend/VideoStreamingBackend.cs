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
    private ILogger? DebugLog => Log.IfEnabled(LogLevel.Debug);

    private IServiceProvider Services { get; }
    private StreamLatencyStore LatencyStore { get; }
    private ILogger Log { get; }

    public VideoStreamingBackend(IServiceProvider services)
    {
        Services = services;
        LatencyStore = services.GetRequiredService<StreamLatencyStore>();
        Log = services.LogFor(GetType());
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

        var filter = new VideoStreamFilter(
            LatencyStore.GetPeerMaxTemporalLayer,
            (sid, ct) => Computed.Capture(() => GetQualityPreset(sid, ct), ct),
            Log);
        return RpcStream.New(filter.Apply(streamId, peerId, skipTo, stream, cancellationToken));
    }

    public virtual async Task<RpcStream<VideoFrame>?> GetVideoRaw(StreamId streamId, CancellationToken cancellationToken)
    {
        Log.LogInformation("GetVideoRaw: #{StreamId}", streamId);
        var stream = await _videoStreams.Get(streamId, cancellationToken).ConfigureAwait(false);
        if (stream == null) {
            Log.LogWarning("GetVideoRaw: #{StreamId} not found in StreamStore", streamId);
            return null;
        }
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

    // Quality control — stream-local state

    // [ComputeMethod]
    public virtual async Task<VideoQualityPreset> GetQualityPreset(StreamId streamId, CancellationToken cancellationToken)
    {
        if (!LatencyStore.LatencyStates.TryGetValue(streamId, out var latencyState))
            return VideoQualityPreset.High;

        // Check if this stream is paused by the priority evaluator (cross-service RPC to ChatId shard)
        // Skip pause check for remote-cached streams that have no ChatId
        if (latencyState.ChatId is not null) {
            var isPaused = await LiveVideoBackend.ShouldPause(latencyState.ChatId, streamId, cancellationToken)
                .ConfigureAwait(false);
            if (isPaused)
                return VideoQualityPreset.Paused;
        }

        var preset = await latencyState.QualityPreset.Use(cancellationToken).ConfigureAwait(false);

        // Consume any pending keyframe request (atomic: only first caller gets it)
        if (LatencyStore.KeyFrameRequests.TryRemove(streamId, out _))
            preset = preset with { IsKeyFrameRequested = true };

        return preset;
    }

    public virtual Task RequestKeyFrame(StreamId streamId, CancellationToken cancellationToken = default)
    {
        if (!LatencyStore.LatencyStates.ContainsKey(streamId)) {
            Log.LogDebug("RequestKeyFrame: streamId={StreamId} — ignored, stream not known locally", streamId);
            return Task.CompletedTask;
        }

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
            _ = GetQualityPreset(streamId, default);

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
        LatencyStore.ReportPeerLatency(streamId, peerId, streamOffsetMs, medianDecodeTimeMs, bufferDepth, bufferSpanMs);
        return Task.CompletedTask;
    }

    // Private methods

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

        // Server-side continuation auto-correlation:
        // If the client didn't explicitly supply ContinuationOf, check whether this author
        // has a recent active stream of the same kind in this chat. If so, the new stream
        // is almost certainly a reconnect / reconfigure of that one. Old streams remain
        // visible in LiveVideoBackend.List for a short grace period after they end (see
        // the finally block below), which covers typical WS reconnect windows.
        var continuationOf = record.ContinuationOf;
        if (continuationOf is null) {
            try {
                var existingStreams = await LiveVideoBackend.List(record.ChatId, cancellationToken).ConfigureAwait(false);
                var recentOwn = existingStreams
                    .Where(s => s.AuthorId == author.Id
                             && s.StreamKind == record.StreamKind
                             && s.StreamId != record.StreamId)
                    .OrderByDescending(s => s.StartedAt)
                    .FirstOrDefault();
                if (recentOwn is not null) {
                    continuationOf = recentOwn.StreamId;
                    Log.LogInformation(
                        "PushVideoInternal: auto-detected continuation #{NewStreamId} <- #{OldStreamId}",
                        record.StreamId, continuationOf);
                }
            }
            catch (Exception e) when (e is not OperationCanceledException) {
                Log.LogWarning(e, "PushVideoInternal: failed to auto-detect continuation; starting fresh");
            }
        }

        // Register stream for real-time signaling
        var streamInfo = new VideoStreamInfo(
            record.StreamId,
            record.ChatId,
            author.Id,
            record.Format,
            beginsAt,
            record.StreamKind,
            continuationOf);

        // Cross-service RPC call — properly shard-routed via ILiveVideoBackend
        await LiveVideoBackend.Register(record.ChatId, streamInfo, cancellationToken)
            .ConfigureAwait(false);

        // When continuing a previous stream, unregister the old one now so viewers
        // see a single active entry per author (with ContinuationOf tag) rather than
        // a brief overlap. The old node's own finally-block Unregister is idempotent.
        if (continuationOf is not null) {
            _ = BackgroundTask.Run(
                () => LiveVideoBackend.Unregister(record.ChatId, continuationOf, CancellationToken.None),
                Log,
                "Failed to unregister continuation source #{StreamId}",
                CancellationToken.None);
        }

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
            // Unregister stream when it ends — after a short grace period so a reconnecting
            // sender landing on a (possibly different) node can still find this stream in
            // LiveVideoBackend.List and auto-correlate it as ContinuationOf. A continuation
            // PushVideo will unregister this entry early on its own.
            // Latency state cleanup deferred to OnVideoStreamExpire — peers may still read buffered frames.
            _ = BackgroundTask.Run(
                async () => {
                    await Task.Delay(Constants.Video.UnregisterGracePeriod, CancellationToken.None).ConfigureAwait(false);
                    await LiveVideoBackend.Unregister(record.ChatId, record.StreamId, CancellationToken.None)
                        .ConfigureAwait(false);
                },
                Log,
                "Failed to unregister stream #{StreamId}",
                CancellationToken.None);
        }
    }

    private void ValidateStreamId(StreamId streamId)
    {
        if (streamId.NodeRef != ThisNode.Ref)
            throw new ArgumentOutOfRangeException(nameof(streamId),
                $"Wrong mesh node: expected {ThisNode.Ref}, but got {streamId.NodeRef}.");
    }
}
