using ActualChat.Diagnostics;
using ActualChat.Streaming.Services;
using ActualChat.Video;
using ActualLab.Rpc;

namespace ActualChat.Streaming;

public class VideoStreamingBackend : IVideoStreamingBackend, IDisposable
{
    private readonly StreamStore<VideoFrame> _videoStreams;

    private static bool DebugMode => Constants.DebugMode.LiveStreaming;

    private MeshNode ThisNode => field ??= Services.MeshWatcher().ThisNode;
    private IChats Chats => field ??= Services.GetRequiredService<IChats>();
    private IAuthors Authors => field ??= Services.GetRequiredService<IAuthors>();
    private MomentClockSet Clocks => field ??= Services.Clocks();
    private ILiveVideoBackend LiveVideoBackend => field ??= Services.GetRequiredService<ILiveVideoBackend>();

    private IServiceProvider Services { get; }
    private ILogger Log { get; }
    private ILogger? DebugLog => DebugMode ? Log : null;

    public VideoStreamingBackend(IServiceProvider services)
    {
        Services = services;
        Log = services.LogFor(GetType());
        var typeFullName = GetType().FullName;
        _videoStreams = new StreamStore<VideoFrame> {
            StreamIdValidator = ValidateStreamId,
            StreamCount = AppMeters.VideoStreamCount,
            ExpirationDelay = Constants.Video.StreamExpirationDelay,
            ReplayTailSize = Constants.Video.ServerReplayTailSize,
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

        // SkipWhile diagnostics: counts non-KF chunks dropped at the head of the
        // returned stream and logs the wait until the first decodable KF surfaces.
        // SkipCount > 0 is direct evidence that the Replay window handed us only
        // deltas — the late-subscriber-wait classic case (e.g. ServerReplayTailSize
        // too narrow for the active simulcast tier count).
        var subscribeAt = CpuTimestamp.Now;
        var skipCount = 0;
        var firstKfLogged = false;
        stream = stream.SkipWhile(f => {
            if (f.IsKeyFrame) {
                if (!firstKfLogged) {
                    firstKfLogged = true;
                    Log.LogInformation(
                        "GetVideoRaw: #{StreamId} first decodable KF after dropping {SkipCount} non-KF chunks in {ElapsedMs:F0}ms",
                        streamId, skipCount, subscribeAt.Elapsed.TotalMilliseconds);
                }
                return false;
            }
            skipCount++;
            return true;
        });
        // Tag any drops the memoizer / SkipWhile introduced between
        // ProcessFrames and this point.
        stream = stream.TraceDrops(FrameDropStage.ServerSkipWhile);

        return new RpcStream<VideoFrame>(stream) {
            AckPeriod = Constants.Video.RpcStreamAckPeriod,
            AckAdvance = Constants.Video.RpcStreamAckAdvance,
        };
    }

    public virtual async Task PushVideo(
        VideoRecord record,
        RpcStream<VideoFrameBundle> videoStream,
        CancellationToken cancellationToken)
    {
        DebugLog?.LogDebug(nameof(PushVideo) + ": record #{StreamId} = {Record}", record.StreamId, record);
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

    // [ComputeMethod] - publisher-facing keyframe-request signal.
    public virtual Task<Moment> LastKeyframeRequestAt(StreamId streamId, CancellationToken cancellationToken)
        => Task.FromResult(Clocks.SystemClock.Now);

    public virtual async Task RequestKeyFrame(StreamId streamId, CancellationToken cancellationToken = default)
    {
        var now = Clocks.SystemClock.Now;
        var requestAt = await LastKeyframeRequestAt(streamId, cancellationToken).ConfigureAwait(false);
        var elapsed = now - requestAt;
        if (elapsed < Constants.Video.KeyFrameRequestCooldown)
            return;

        Log.LogInformation("RequestKeyFrame: streamId={StreamId}", streamId);
        using (Invalidation.Begin())
            _ = LastKeyframeRequestAt(streamId, default);
    }

    // Private methods

    private async Task PushVideoInternal(
        VideoRecord record,
        IAsyncEnumerable<VideoFrameBundle> videoBundles,
        CancellationToken cancellationToken)
    {
        using var watchdogCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cancellationToken = watchdogCts.Token;

        // Compatibility note: VideoRecord.ClientStartAt is the legacy RPC/record
        // name. The value itself is source time on the server-synced clock.
        var sourceStartOffsetSeconds = record.ClientStartAt;
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

        // Register stream for real-time signaling. Initially we only know the
        // The producer registers with the top-tier format (highest layer
        // dims + codec). The simulcast ladder for non-top layers is
        // derivable from `SourceKind` via the recorder's ladder builder.
        var streamInfo = new VideoStreamInfo(
            record.StreamId,
            record.ChatId,
            author.Id,
            record.Format,
            beginsAt,
            record.SourceKind,
            sourceStartedAt);

        // Cross-service RPC call — properly shard-routed via ILiveVideoBackend
        await LiveVideoBackend.Register(record.ChatId, streamInfo, cancellationToken)
            .ConfigureAwait(false);

        Task? silenceWatchdogTask = null;
        try {
            // Publish video stream for real-time viewing
            // No processing - just forward to StreamStore for memoization
            Log.LogInformation("PushVideoInternal: publishing #{StreamId} to StreamStore", record.StreamId);

            var startedLayers = new HashSet<int>();
            var negativeOffsetDropCount = 0;
            var preKeyframeDeltaDropCount = 0;
            var lastHeartbeat = CpuTimestamp.Now;
            var heartbeatInterval = Constants.Video.SilenceGracePeriod / 2;

            // Stream-silence watchdog: counts incoming bundles per fixed
            // interval and cancels watchdogCts after K consecutive zero
            // intervals (default 2 × 5 s = 10 s). Catches "browser tab
            // closed without a clean WebSocket close" fast enough to free
            // the single-screencast-per-chat slot. One increment per
            // bundle (= source moment), regardless of layer count.
            var bundleCounter = 0;
            var streamId = record.StreamId;
            // CA2025: the watchdog task is awaited in the outer finally
            // (silenceWatchdogTask is hoisted there) so it always completes
            // before `watchdogCts` is disposed. The analyzer can't track
            // that across the variable hoist.
#pragma warning disable CA2025
            silenceWatchdogTask = StreamSilenceWatchdog.Run(
                consumeFrameCount: () => Interlocked.Exchange(ref bundleCounter, 0),
                onSilenceCts: watchdogCts,
                interval: Constants.Video.StreamSilenceCheckInterval,
                maxConsecutiveZeroIntervals: Constants.Video.StreamSilenceMaxConsecutiveZeroIntervals,
                onSilenceDetected: zeroIntervals => Log.LogWarning(
                    "Stale stream watchdog: no bundles in {DurationSec}s for stream #{StreamId}, closing",
                    Constants.Video.StreamSilenceCheckInterval.TotalSeconds * zeroIntervals, streamId),
                cancellationToken: cancellationToken);
#pragma warning restore CA2025

            async IAsyncEnumerable<VideoFrame> ProcessFrames(IAsyncEnumerable<VideoFrameBundle> source)
            {
                // Bundle-level drop trace: any gap between consecutive bundle
                // indexes is attributed to ServerPushStream (RpcStream sender
                // ring compaction or wire/auth failure) and the new entries
                // are written into every layer's per-frame trace.
                source = source.TraceBundleDrops(FrameDropStage.ServerPushStream);
                await foreach (var bundle in source.WithCancellation(cancellationToken).ConfigureAwait(false)) {
                    Interlocked.Increment(ref bundleCounter);
                    if (bundle.Layers.Length == 0)
                        continue;

                    // Decompose: each per-layer VideoFrame is processed and yielded
                    // independently. Memoizer + filter + GetStream stay per-frame on
                    // the consumer side; the bundle exists only on the publisher leg
                    // for wire-format efficiency.
                    foreach (var frame in bundle.Layers) {
                        var layerId = frame.LayerId;
                        if (frame.Offset < TimeSpan.Zero) {
                            negativeOffsetDropCount++;
                            if (negativeOffsetDropCount <= 3 || negativeOffsetDropCount % 30 == 0)
                                Log.LogWarning(
                                    "ProcessFrames: dropping frame with negative offset ({DropCount}) for stream #{StreamId}: {Frame}",
                                    negativeOffsetDropCount, record.StreamId, frame);
                            continue;
                        }

                        if (bundle.IsKeyFrame) {
                            if (startedLayers.Add(layerId))
                                Log.LogWarning(
                                    "ProcessFrames: first keyframe for stream #{StreamId}: {Frame}",
                                    record.StreamId, frame);
                        }
                        else if (!startedLayers.Contains(layerId)) {
                            preKeyframeDeltaDropCount++;
                            if (preKeyframeDeltaDropCount <= 3 || preKeyframeDeltaDropCount % 30 == 0)
                                Log.LogWarning(
                                    "ProcessFrames: dropping delta frame ({DropCount}) for stream #{StreamId}: {Frame}",
                                    preKeyframeDeltaDropCount, record.StreamId, frame);
                            continue;
                        }

                        // Stamp arrival wallclock for downlink-latency attribution
                        // on receivers; invalidate the cached serialization so the
                        // first fan-out consumer rebuilds bytes including the stamp.
                        frame.ServerArrivedAtTicks = DateTime.UtcNow.Ticks;
                        frame.SerializedData = default;
                        yield return frame;
                    }

                    if (lastHeartbeat.Elapsed >= heartbeatInterval) {
                        lastHeartbeat = CpuTimestamp.Now;
                        // This call is idempotent and just bumps expiration
                        await LiveVideoBackend.Register(record.ChatId, streamInfo, CancellationToken.None)
                            .ConfigureAwait(false);
                    }
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
                ProcessFrames(videoBundles),
                Constants.Video.ServerReplayTailDuration,
                cancellationToken);
            if (_videoStreams.Publish(record.StreamId, memoizer))
                await (memoizer.WhenRunning ?? Task.CompletedTask).ConfigureAwait(false);
            else
                await memoizer.DisposeAsync().ConfigureAwait(false);
        }
        finally {
            // Wake the silence watchdog explicitly so it doesn't keep us
            // here for up to one full interval (5 s) on a clean source
            // end. The watchdog's Task.Delay is bound to
            // cancellationToken (= watchdogCts.Token); cancelling here
            // releases it immediately. Safe at this point: ProcessFrames
            // and the memoizer have already wound down, so nothing else
            // is reading watchdogCts.Token.
            if (silenceWatchdogTask is not null) {
                try { await watchdogCts.CancelAsync().ConfigureAwait(false); }
                catch { /* already cancelled / disposed */ }
                try {
                    await silenceWatchdogTask.ConfigureAwait(false);
                }
                catch (OperationCanceledException) { /* expected on teardown */ }
            }

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
