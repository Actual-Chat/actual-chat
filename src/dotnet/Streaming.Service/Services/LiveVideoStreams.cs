using ActualChat.Diagnostics;
using ActualChat.Video;
using ActualLab.Rpc;
using Microsoft.Extensions.Hosting;

namespace ActualChat.Streaming.Services;

public class LiveVideoStreams : ILiveVideoStreams
{
    private static bool DebugMode => Constants.DebugMode.LiveStreaming;
    private static readonly TimeSpan ReceiveQualityRetention = TimeSpan.FromMinutes(3);
    private static readonly TimeSpan ReceiveQualityCleanupPeriod = TimeSpan.FromMinutes(5);

    private IServiceProvider Services { get; }
    private MeshWatcher MeshWatcher { get; }
    private IHostApplicationLifetime HostLifetime { get; }
    private ILiveVideoBackend Backend { get; }
    private IVideoStreamingBackend VideoStreamingBackend { get; }
    private RemoteVideoStreamCache RemoteVideoCache { get; }
    private IChats Chats { get; }
    private MomentClock SystemClock { get; }
    private ILogger Log { get; }
    private ILogger? DebugLog => DebugMode ? Log : null;

    private readonly ConcurrentDictionary<Session, ReceiveQualityState> _qualityBySession = new();
    // ReSharper disable once NotAccessedField.Local
    private readonly Task _qualityBySessionCleanupTask;

    public LiveVideoStreams(IServiceProvider services)
    {
        Services = services;
        Log = Services.LogFor(GetType());
        MeshWatcher = services.MeshWatcher();
        HostLifetime = services.HostLifetime();
        Backend = services.GetRequiredService<ILiveVideoBackend>();
        VideoStreamingBackend = services.GetRequiredService<IVideoStreamingBackend>();
        RemoteVideoCache = services.GetRequiredService<RemoteVideoStreamCache>();
        Chats = services.GetRequiredService<IChats>();
        SystemClock = Services.Clocks().SystemClock;

        _qualityBySessionCleanupTask = BackgroundTask.Run(
            () => RunQualityCleanup(HostLifetime.ApplicationStopping),
            Log, "LiveVideoStreams quality cleanup failed", HostLifetime.ApplicationStopping);
    }

    // [ComputeMethod]
    public virtual async Task<ApiArray<VideoStreamInfo>> List(
        Session session,
        ChatId chatId,
        CancellationToken cancellationToken)
    {
        var chatRules = await Chats.GetRules(session, chatId, cancellationToken).ConfigureAwait(false);
        if (!chatRules.Has(ChatPermissions.ReadVideo))
            return [];

        var result = await Backend.List(chatId, cancellationToken).ConfigureAwait(false);
        Log.LogDebug("ListActiveStreams(session, {ChatId}): returning {Count} streams", chatId, result.Count);
        return result;
    }

    // [ComputeMethod]
    public virtual async Task<int> GetMemberCount(
        Session session,
        ChatId chatId,
        CancellationToken cancellationToken)
    {
        var chatRules = await Chats.GetRules(session, chatId, cancellationToken).ConfigureAwait(false);
        if (!chatRules.Has(ChatPermissions.ReadVideo))
            return 0;

        return await Backend.GetVideoStreamMemberCount(chatId, cancellationToken).ConfigureAwait(false);
    }

    public async Task RegisterMember(
        Session session,
        ChatId chatId,
        ApiArray<string> supportedDecoderCodecs,
        CancellationToken cancellationToken)
    {
        var chatRules = await Chats.GetRules(session, chatId, cancellationToken).ConfigureAwait(false);
        chatRules.Require(ChatPermissions.ReadVideo);
        await Backend.RegisterMember(chatId, session.Id, supportedDecoderCodecs, cancellationToken).ConfigureAwait(false);
    }

    public async Task UnregisterMember(
        Session session,
        ChatId chatId,
        CancellationToken cancellationToken)
    {
        var chatRules = await Chats.GetRules(session, chatId, cancellationToken).ConfigureAwait(false);
        chatRules.Require(ChatPermissions.ReadVideo);
        await Backend.UnregisterMember(chatId, session.Id, cancellationToken).ConfigureAwait(false);
    }

    // [ComputeMethod]
    public virtual async Task<ApiArray<string>> GetSupportedCodecs(
        Session session,
        ChatId chatId,
        CancellationToken cancellationToken)
    {
        var chatRules = await Chats.GetRules(session, chatId, cancellationToken).ConfigureAwait(false);
        chatRules.Require(ChatPermissions.ReadVideo);
        return await Backend.GetSupportedCodecs(chatId, cancellationToken).ConfigureAwait(false);
    }

    public async Task<RpcStream<VideoFrame>?> GetStream(
        Session session,
        StreamId streamId,
        CancellationToken cancellationToken)
    {
        // Stream-level access is gated upstream via List/RegisterMember.
        var streamIdValue = streamId.Value;
        var isLocal = streamId.NodeRef == MeshWatcher.ThisNode.Ref;

        // Local: backend's per-shard StreamStore already memoizes; double-caching
        // here would just waste memory. Remote: fan out via RemoteVideoStreamCache
        // so N viewers across this API server collapse to one cross-shard pull.
        var rawStream = isLocal
            ? await VideoStreamingBackend.GetVideoRaw(streamId, cancellationToken).ConfigureAwait(false)
            : await GetOrFetchRemoteVideo(streamId, cancellationToken).ConfigureAwait(false);
        if (rawStream is null)
            return null;

        // Defense in depth: the memoizer cache normally serves an instant
        // first frame from the latest keyframe anchor; PLI primes a fresher
        // keyframe in parallel for the cases where the cached anchor is near
        // KeyFramePeriod old or the cache is empty (subscribed before first
        // KF). RequestKeyFrame is internally rate-limited via
        // KeyFrameRequestCooldown so concurrent joiners collapse to one PLI.
        // Token is None: a viewer disconnect must not void a PLI that may
        // benefit other viewers.
        _ = VideoStreamingBackend.RequestKeyFrame(streamId, CancellationToken.None);

        var filtered = ReceiveQualityFilter.Apply(
            rawStream,
            () => GetReceiveQuality(session, streamIdValue),
            Log,
            cancellationToken);

        // Diagnostic: time from GetStream entry to the first frame the
        // server-side RpcStream actually yields. A large gap here means
        // either the upstream (memoizer) is slow to surface a frame or
        // the receive-quality filter dropped everything until a matching
        // frame appeared. Pairs with the receiver-side first-frame log
        // to localize post-visibility-restore stalls.
        var subscribedAt = CpuTimestamp.Now;
        return new RpcStream<VideoFrame>(LogFirstFrame()) {
            AllowReconnect = false,
            AckPeriod = Constants.Video.RpcStreamAckPeriod,
            BufferSize = Constants.Video.RpcStreamBufferSize,
        };

        async IAsyncEnumerable<VideoFrame> LogFirstFrame()
        {
            var first = true;
            await foreach (var f in filtered.ConfigureAwait(false)) {
                if (first) {
                    first = false;
                    Log.LogInformation(
                        "GetStream: first frame yielded to RpcStream session={Session} streamId={StreamId} in {ElapsedMs:F0}ms",
                        session, streamIdValue, subscribedAt.Elapsed.TotalMilliseconds);
                }
                yield return f;
            }
        }
    }

    // [ComputeMethod]
    public virtual async Task<Moment> LastKeyframeRequestAt(
        Session session,
        StreamId streamId,
        CancellationToken cancellationToken)
        => await VideoStreamingBackend.LastKeyframeRequestAt(streamId, cancellationToken).ConfigureAwait(false);

    public async Task PushStream(
        Session session,
        string chatId,
        double clientStartAt, // Unix epoch (seconds, double)
        VideoFormat format,
        VideoSourceKind sourceKind,
        RpcStream<VideoFrame> frameStream,
        CancellationToken cancellationToken)
    {
        // Live video calls: cap at Constants.Video.MaxLiveDuration (8h) rather than
        // the 3-min chat-entry duration. Every VideoSourceKind (Camera/ScreenCast) is a
        // live stream; there is no voice-message-style video path.
        using var stopCts = new CancellationTokenSource(Constants.Video.MaxLiveDuration);
        try {
            var chatIdTyped = ChatId.Parse(chatId);
            var streamId = StreamId.New(MeshWatcher.ThisNode.Ref);
            var videoRecord = new VideoRecord(streamId, session, chatIdTyped, clientStartAt, format, sourceKind);
            Log.LogInformation("PushStream: {VideoRecord}", videoRecord);

            var newFrameStream = RpcStream.New(frameStream);
            await VideoStreamingBackend.PushVideo(videoRecord, newFrameStream, stopCts.Token).ConfigureAwait(false);
        }
        finally {
            // Release the remote sender on method exit so its writeFrom doesn't hang.
            frameStream.Disconnect();
        }
    }

    public Task RequestKeyFrame(Session session, string streamId, CancellationToken cancellationToken)
    {
        _ = session;
        var sid = StreamId.Parse(streamId);
        return RpcNoWait.Tasks.From(VideoStreamingBackend.RequestKeyFrame(sid, cancellationToken));
    }

    public Task ChangeRecordingQuality(
        Session session,
        RecordingQualityState? state,
        RecordingQualityInfo? info,
        CancellationToken cancellationToken)
    {
        DebugLog?.LogDebug("ChangeRecordingQuality: session={Session}, state={State}, info={Info}", session, state, info);

        if (info?.Health is { } h) {
            AppMeters.VideoSendEncodeRatio.Record(h.EncodeRatioP90);
            AppMeters.VideoSendDropRatio.Record(h.SenderFrameDropRatioEma);
            // -1 marks "no ACK observed yet" — don't pollute the histogram with a sentinel.
            if (h.LastAckAgeMs >= 0)
                AppMeters.VideoSendAckAgeMs.Record(h.LastAckAgeMs);
        }
        if (state is not null)
            AppMeters.VideoSendLayerCount.Record(state.EffectiveLayerCount);

        return RpcNoWait.Tasks.Completed;
    }

    public async Task ChangePlaybackQuality(
        Session session,
        ApiMap<string, ReceiveQuality>? qualityByStream,
        PlaybackQualityInfo? info,
        CancellationToken cancellationToken)
    {
        if (qualityByStream is null) {
            _qualityBySession.TryRemove(session, out _);
            DebugLog?.LogDebug("ChangePlaybackQuality: session={Session}, info={Info} (cleared)", session, info);
            return;
        }

        qualityByStream = ApplyStreamCountCap(qualityByStream, info);
        _qualityBySession.TryGetValue(session, out var prevState);
        _qualityBySession[session] = new ReceiveQualityState(qualityByStream, SystemClock.Now);
        DebugLog?.LogDebug("ChangePlaybackQuality: session={Session}, streams={Count}, info={Info}",
            session, qualityByStream.Count, info);

        if (info is not null) {
            AppMeters.VideoReceiveCapacityBps.Record(info.EstimatedCapacityBytesPerSec);
            AppMeters.VideoReceiveAggregateHealth.Record(info.AggregateHealth);
            foreach (var (_, s) in info.Streams) {
                var priorityTag = PriorityTag(s.Priority);
                // Capture-to-presentation latency, server-clock-corrected by
                // the receiver. Skip the default-0 sample from clients still
                // on the old wire format — recording 0 ms would skew the
                // histogram toward an unrealistic floor.
                if (s.LatencyMsEma > 0)
                    AppMeters.VideoLatency.Record(s.LatencyMsEma, priorityTag);
                if (s.KeyframeSkipsInWindow > 0)
                    AppMeters.VideoReceiveKeyframeSkips.Add(s.KeyframeSkipsInWindow, priorityTag);
                AppMeters.VideoReceiveDecoderQueue.Record(s.DecoderQueueDepthEma, priorityTag);
            }
        }

        // Request a fresh keyframe whenever a stream's quality envelope CHANGES,
        // not just on lowering. On upgrades, the new layer/temporal layer
        // can't be decoded by the receiver until the next keyframe of that
        // layer arrives — periodic keyframes are 3s apart, so without this we
        // get up to 3s of stuck-on-old-quality after every upgrade. The
        // VideoStreamingBackend.RequestKeyFrame path is throttled (≥5 s gap),
        // so this won't burst even if many receivers change at once.
        var keyFrameRequests = GetChangedStreams(prevState?.QualityByStream, qualityByStream)
            .Select(x => VideoStreamingBackend.RequestKeyFrame(StreamId.Parse(x), cancellationToken))
            .ToArray();
        if (keyFrameRequests.Length != 0)
            await Task.WhenAll(keyFrameRequests).ConfigureAwait(false);
    }

    // Private methods

    private async Task<IAsyncEnumerable<VideoFrame>?> GetOrFetchRemoteVideo(
        StreamId streamId, CancellationToken cancellationToken)
    {
        var store = RemoteVideoCache.Store;

        // Cache hit fast-path: replay from existing memoizer. The replay tail
        // can begin mid-GOP, so SkipWhile drops deltas until the next keyframe
        // — matches GetVideoRaw's keyframe-anchor semantics on the backend.
        var stream = await store.Get(streamId, false, cancellationToken).ConfigureAwait(false);
        if (stream != null)
            return stream.SkipWhile(f => !f.IsKeyFrame);

        // Miss: pull once from the publisher's shard, cache for siblings.
        // CT.None deliberately: detaches the cached source from this viewer's
        // lifetime so V1 disconnect cannot tear the cached stream down for
        // V2..VN. Cache entry's idle expiry handles cleanup of unowned streams.
        var rawRpcStream = await VideoStreamingBackend
            .GetVideoRaw(streamId, CancellationToken.None)
            .ConfigureAwait(false);
        if (rawRpcStream == null)
            return null;

        Log.LogInformation("GetOrFetchRemoteVideo: caching #{StreamId} locally", streamId);
        // Bounded memoizer: per-layer keyframe-span eviction caps
        // retained content at ~ServerReplayTailDuration per layer, regardless
        // of stream duration. Without this, an 8-hour call would grow the
        // chain to millions of nodes.
        var memoizer = new VideoStreamMemoizer(
            rawRpcStream,
            Constants.Video.ServerReplayTailDuration,
            CancellationToken.None);
        if (!store.Publish(streamId, memoizer))
            // Lost the registration race to a concurrent first-viewer. Dispose
            // our memoizer so its Read loop tears down and the duplicate
            // cross-shard RPC closes immediately.
            await memoizer.DisposeAsync().ConfigureAwait(false);

        var shared = await store.Get(streamId, true, cancellationToken).ConfigureAwait(false);
        return shared?.SkipWhile(f => !f.IsKeyFrame);
    }

    private async Task RunQualityCleanup(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested) {
            await Task.Delay(ReceiveQualityCleanupPeriod, cancellationToken).ConfigureAwait(false);
            CleanupQualityBySession();
        }
        return;

        void CleanupQualityBySession() {
            var threshold = SystemClock.Now - ReceiveQualityRetention;
            foreach (var kv in _qualityBySession)
                if (kv.Value.UpdatedAt < threshold)
                    _qualityBySession.TryRemove(kv);
        }
    }

    private ReceiveQuality GetReceiveQuality(Session session, string streamId)
        => _qualityBySession.TryGetValue(session, out var state)
            ? state.QualityByStream.TryGetValue(streamId, out var quality)
                ? quality
                : ReceiveQuality.Lowest
            : ReceiveQuality.Default;

    private static IEnumerable<string> GetChangedStreams(
        ApiMap<string, ReceiveQuality>? previous,
        ApiMap<string, ReceiveQuality> current)
    {
        foreach (var (streamId, quality) in current) {
            var oldQuality = previous is not null && previous.TryGetValue(streamId, out var old)
                ? old
                : ReceiveQuality.Default;
            if (quality.MaxLayerId != oldQuality.MaxLayerId
                || quality.MaxTemporalLayerId != oldQuality.MaxTemporalLayerId)
                yield return streamId;
        }
    }

    private static ApiMap<string, ReceiveQuality> ApplyStreamCountCap(
        ApiMap<string, ReceiveQuality> qualityByStream,
        PlaybackQualityInfo? info)
    {
        const int serverCap = 9;
        var aboveLowest = qualityByStream.Where(kv => !kv.Value.IsLowest).ToList();
        if (aboveLowest.Count <= serverCap)
            return qualityByStream;

        var demotedStreamIds = aboveLowest
            .Select((kv, index) => (kv, rank: PriorityRank(kv.Key), index))
            .OrderBy(x => (x.rank, x.index))
            .Take(aboveLowest.Count - serverCap)
            .Select(kv => kv.kv.Key)
            .ToHashSet();

        var result = new ApiMap<string, ReceiveQuality>();
        foreach (var (streamId, quality) in qualityByStream)
            result[streamId] = demotedStreamIds.Contains(streamId) ? ReceiveQuality.Lowest : quality;
        return result;

        // Demote secondaries before primaries; preserve request order otherwise.
        // info.Streams[streamId].Priority cross-reference falls back to Secondary.
        int PriorityRank(string streamId) {
            if (info is null || !info.Streams.TryGetValue(streamId, out var streamInfo))
                return 0; // Secondary
            return streamInfo.Priority == PlaybackStreamPriority.Primary ? 1 : 0;
        }
    }

    private static KeyValuePair<string, object?> PriorityTag(PlaybackStreamPriority priority)
        => new("priority", priority == PlaybackStreamPriority.Primary ? "primary" : "secondary");

    // Nested types

    private sealed record ReceiveQualityState(
        ApiMap<string, ReceiveQuality> QualityByStream,
        Moment UpdatedAt);
}
