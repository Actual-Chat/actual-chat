using ActualChat.Diagnostics;
using ActualChat.Streaming.Services;
using ActualChat.Video;
using ActualLab.Rpc;

namespace ActualChat.Streaming;

public class VideoStreamingBackend : IVideoStreamingBackend, IDisposable
{
    private readonly StreamStore<VideoFrame> _videoStreams;

    private MeshNode ThisNode => field ??= Services.MeshWatcher().ThisNode;
    private IChats Chats => field ??= Services.GetRequiredService<IChats>();
    private IAuthors Authors => field ??= Services.GetRequiredService<IAuthors>();
    private MomentClockSet Clocks => field ??= Services.Clocks();
    private ILiveVideoBackend LiveVideoBackend => field ??= Services.GetRequiredService<ILiveVideoBackend>();

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
            ReplayTailSize = Constants.Video.ServerReplayTailSize,
            OnStreamExpire = OnVideoStreamExpire,
            Log = services.LogFor($"{typeFullName}.VideoStreams"),
        };
    }

    public void Dispose()
        => _videoStreams.Dispose();

    public virtual async Task<RpcStream<VideoFrame>?> GetVideoRaw(StreamId streamId, CancellationToken cancellationToken)
    {
        Log.LogInformation("GetVideoRaw: #{StreamId}", streamId);
        var stream = await _videoStreams.Get(streamId, cancellationToken).ConfigureAwait(false);
        if (stream == null) {
            Log.LogWarning("GetVideoRaw: #{StreamId} not found in StreamStore", streamId);
            return null;
        }

        stream = stream.SkipWhile(f => !f.IsKeyFrame);

        return new RpcStream<VideoFrame>(stream) {
            AckPeriod = Constants.Video.RpcStreamAckPeriod,
            BufferSize = Constants.Video.RpcStreamBufferSize,
        };
    }

    public virtual async Task PushVideo(
        VideoRecord record,
        RpcStream<VideoFrame> videoStream,
        CancellationToken cancellationToken)
    {
        Log.LogTrace(nameof(PushVideo) + ": record #{StreamId} = {Record}", record.StreamId, record);
        var delayedCts = cancellationToken.CreateDelayedTokenSource(Constants.Video.CancellationDelay);
        var delayedCancellationToken = delayedCts.Token;
        try {
            ValidateStreamId(record.StreamId);
            await PushVideoInternal(record, videoStream, delayedCancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception e) when (e is not OperationCanceledException) {
            Log.LogError(e, "PushVideo failed for stream #{StreamId}", record.StreamId);
            throw;
        }
        finally {
            // Release the producer's sender: once PushVideo returns nobody will
            // pull from `videoStream` again, so the far end must stop buffering.
            videoStream.Disconnect();
            delayedCts.CancelAndDisposeSilently();
        }
    }

    // [ComputeMethod] — publisher-facing keyframe-request signal. The old
    // quality-adaptation logic was removed in Step 8.5; this method now only
    // surfaces pending keyframe requests. RequestKeyFrame stores the flag and
    // invalidates this Computed; the publisher's subscription re-reads it,
    // observes IsKeyFrameRequested=true, and forces the next frame to be a KF.
    public virtual Task<VideoQualityPreset> GetQualityPreset(StreamId streamId, CancellationToken cancellationToken)
    {
        _ = cancellationToken;
        var preset = VideoQualityPreset.High;
        if (LatencyStore.KeyFrameRequests.TryRemove(streamId, out _))
            preset = preset with { IsKeyFrameRequested = true };
        return Task.FromResult(preset);
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
            _ = GetQualityPreset(streamId, default);

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
        using var watchdogCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cancellationToken = watchdogCts.Token;

        // Compatibility note: VideoRecord.ClientStartOffset is the legacy RPC/record
        // name. The value itself is source time on the server-synced clock.
        var sourceStartOffsetSeconds = record.ClientStartOffset;
        var sourceStartedAt = default(Moment) + TimeSpan.FromSeconds(sourceStartOffsetSeconds);
        var beginsAt = sourceStartedAt;
        var rules = await Chats.GetRules(record.Session, record.ChatId, cancellationToken)
            .ConfigureAwait(false);
        rules.Require(ChatPermissions.Write);
        rules.Require(ChatPermissions.WriteVideo);

        var author = await Authors
            .EnsureJoined(record.Session, record.ChatId, cancellationToken)
            .ConfigureAwait(false);

        // Guard against source clock skew: if sourceStartedAt is too far from server time,
        // override with server time to prevent false latency reports and quality step-downs.
        var serverNow = Clocks.ServerClock.Now;
        var clockDelta = serverNow - beginsAt;
        if (Math.Abs(clockDelta.TotalSeconds) > 5) {
            Log.LogWarning("TIMING_ANCHOR: StreamId={StreamId}, source clock skew={ClockDeltaMs:F0}ms, overriding sourceStartedAt with server time",
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
            record.StreamKind,
            sourceStartedAt);

        // Cross-service RPC call — properly shard-routed via ILiveVideoBackend
        await LiveVideoBackend.Register(record.ChatId, streamInfo, cancellationToken)
            .ConfigureAwait(false);

        try {
            // Publish video stream for real-time viewing
            // No processing - just forward to StreamStore for memoization
            Log.LogInformation("PushVideoInternal: publishing #{StreamId} to StreamStore", record.StreamId);

            // Per-(spatial-layer) keyframe counters. Simulcast senders emit a
            // keyframe on every spatial layer at the same boundary; a single
            // global counter would give sibling-layer KFs different numbers,
            // and deltas would be stamped with whichever layer's KF landed last
            // — making downstream gap detection (filter compares KeyFrameNumber
            // equality to decide "this delta belongs to the KF I joined at")
            // fire spuriously. Keeping counters per-layer keeps the "delta.kf
            // == lastYieldedKf" invariant correct for the filter's selected
            // layer. Small key = int (spatialLayerId, typically 0..2).
            var keyFrameNumberByLayer = new Dictionary<int, long>();
            var lastHeartbeat = CpuTimestamp.Now;
            var heartbeatInterval = TimeSpan.FromMinutes(2.5); // Half of LiveVideoBackend.ChatStateTtl
            var silenceTimeout = record.StreamKind == StreamKind.Screencast
                ? Constants.Video.ScreencastFrameSilenceTimeout
                : Constants.Video.WebcamFrameSilenceTimeout;
            async IAsyncEnumerable<VideoFrame> ProcessFrames(IAsyncEnumerable<VideoFrame> source)
            {
                // Frame-silence watchdog: cancels watchdogCts if no frame arrives within silenceTimeout.
                // Each frame resets the deadline; CancellationTokenSource reuses a single internal timer.
                watchdogCts.CancelAfter(silenceTimeout);
                await foreach (var frame in source.WithCancellation(cancellationToken).ConfigureAwait(false)) {
                    watchdogCts.CancelAfter(silenceTimeout);

                    var layerId = frame.SpatialLayerId;
                    if (frame.IsKeyFrame) {
                        keyFrameNumberByLayer.TryGetValue(layerId, out var current);
                        keyFrameNumberByLayer[layerId] = current + 1;
                    }
                    keyFrameNumberByLayer.TryGetValue(layerId, out var layerKf);
                    frame.KeyFrameNumber = layerKf;

                    if (lastHeartbeat.Elapsed >= heartbeatInterval) {
                        lastHeartbeat = CpuTimestamp.Now;
                        // This call is idempotent and just bumps expiration
                        await LiveVideoBackend.Register(record.ChatId, streamInfo, CancellationToken.None)
                            .ConfigureAwait(false);
                    }

                    yield return frame;
                }
            }

            // VideoFrame.SerializedData is a plain GC-managed byte[], released automatically
            // when all consumers (including lagging ones via the linked-list AsyncMemoizer)
            // release their references — no eviction callback / pooled-buffer lifecycle needed.
            //
            // Per-layer keyframe-span retention bounded by ServerReplayTailDuration
            // (see docs/video-pipeline.md "Server stream store"). Replaces the
            // previous count-based RetentionBufferSize policy: duration-tracked
            // eviction drops complete keyframe-anchored spans rather than
            // individual frames, so deltas are never orphaned in retention.
            var memoizer = new VideoStreamMemoizer(
                ProcessFrames(videoFrames),
                Constants.Video.ServerReplayTailDuration,
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
