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

        // Trace memoizer-side drops first, then the per-consumer quality filter.
        var traced = rawStream.TraceDrops(FrameDropStage.ServerMemoizer);
        var filtered = ReceiveQualityFilter.Apply(
            traced,
            () => GetReceiveQuality(session, streamIdValue),
            Log,
            cancellationToken);
        filtered = filtered.TraceDrops(FrameDropStage.ServerReceiveQualityFilter);

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
            AckAdvance = Constants.Video.RpcStreamAckAdvance,
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

    // [ComputeMethod]
    // Cross-node aggregate: delegates to VideoStreamingBackend on the stream's
    // owning node, which receives demand reports from every API pod.
    public virtual async Task<int> MaxRequestedLayerId(
        Session session,
        StreamId streamId,
        CancellationToken cancellationToken)
        // `session` is unused in the result (the aggregate spans every viewer),
        // so it must NOT take part in the cache key — otherwise a viewer's
        // ChangePlaybackQuality invalidates its OWN session's key while the
        // producer subscribes under the producer's session key, and the producer
        // never sees the change (stuck at its startup value). Delegate to a
        // session-less inner compute instead.
        => await MaxRequestedLayerIdByStream(streamId, cancellationToken).ConfigureAwait(false);

    [ComputeMethod]
    public virtual async Task<int> MaxRequestedLayerIdByStream(
        StreamId streamId,
        CancellationToken cancellationToken)
    {
        var info = await DemandInfoByStream(streamId, cancellationToken).ConfigureAwait(false);
        return MaxLayerIdFromMask(info.Mask);
    }

    // [ComputeMethod]
    public virtual async Task<int> RequestedLayersMask(
        Session session,
        StreamId streamId,
        CancellationToken cancellationToken)
        // Same session-key caveat as MaxRequestedLayerId: the aggregate spans
        // every viewer, so delegate to a session-less inner compute.
        => await RequestedLayersMaskByStream(streamId, cancellationToken).ConfigureAwait(false);

    [ComputeMethod]
    public virtual async Task<int> RequestedLayersMaskByStream(
        StreamId streamId,
        CancellationToken cancellationToken)
    {
        var info = await DemandInfoByStream(streamId, cancellationToken).ConfigureAwait(false);
        return info.Mask;
    }

    // [ComputeMethod]
    public virtual async Task<bool> ThumbnailViewersOnly(
        Session session,
        StreamId streamId,
        CancellationToken cancellationToken)
        // Same session-key caveat as MaxRequestedLayerId: the aggregate spans
        // every viewer, so delegate to a session-less inner compute.
        => await ThumbnailViewersOnlyByStream(streamId, cancellationToken).ConfigureAwait(false);

    [ComputeMethod]
    public virtual async Task<bool> ThumbnailViewersOnlyByStream(
        StreamId streamId,
        CancellationToken cancellationToken)
    {
        var info = await DemandInfoByStream(streamId, cancellationToken).ConfigureAwait(false);
        return info.ThumbnailViewersOnly;
    }

    // [ComputeMethod]
    public virtual async Task<StreamDemandInfo> DemandInfo(
        Session session,
        StreamId streamId,
        CancellationToken cancellationToken)
        // Same session-key caveat as MaxRequestedLayerId: the aggregate spans
        // every viewer, so delegate to a session-less inner compute.
        => await DemandInfoByStream(streamId, cancellationToken).ConfigureAwait(false);

    [ComputeMethod]
    public virtual async Task<StreamDemandInfo> DemandInfoByStream(
        StreamId streamId,
        CancellationToken cancellationToken)
        => await VideoStreamingBackend.DemandInfo(streamId, cancellationToken).ConfigureAwait(false);

    public async Task PushStream(
        Session session,
        string chatId,
        double clientStartAt, // Unix epoch (seconds, double)
        VideoFormat format,
        VideoSourceKind sourceKind,
        RpcStream<VideoFrameBundle> frameStream,
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

        if (info is not null) {
            AppMeters.VideoSendDropRatio.Record(info.SenderFrameDropRatioEma);
            // -1 marks "no ACK observed yet" — don't pollute the histogram with a sentinel.
            if (info.LastAckAgeMs >= 0)
                AppMeters.VideoSendAckAgeMs.Record(info.LastAckAgeMs);
            AppMeters.VideoSendThermalLevel.Record((int)info.ThermalLevel);
            if (!info.IsHardwareAccelerated)
                AppMeters.VideoSendSoftwareEncode.Add(1);
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
            if (_qualityBySession.TryRemove(session, out var removing))
                await ForwardDemandChanges(session, removing.QualityByStream, null, cancellationToken)
                    .ConfigureAwait(false);
            DebugLog?.LogDebug("ChangePlaybackQuality: session={Session}, info={Info} (cleared)", session, info);
            return;
        }

        qualityByStream = ApplyStreamCountCap(qualityByStream, info);
        _qualityBySession.TryGetValue(session, out var prevState);
        _qualityBySession[session] = new ReceiveQualityState(qualityByStream, SystemClock.Now);
        // The demand aggregates live on each stream's owning node
        // (VideoStreamingBackend); forward this viewer's per-stream demand
        // there. Every entry is forwarded — not just changed ones — so the
        // backend's retention stamp is refreshed by the 1-min keepalive.
        await ForwardDemandChanges(session, prevState?.QualityByStream, qualityByStream, cancellationToken)
            .ConfigureAwait(false);
        DebugLog?.LogDebug("ChangePlaybackQuality: session={Session}, streams={Count}, info={Info}",
            session, qualityByStream.Count, info);

        if (info is not null) {
            AppMeters.VideoReceiveCapacityBps.Record(info.EstimatedCapacityBytesPerSec);
            AppMeters.VideoReceiveAggregateHealth.Record(info.AggregateHealth);
        }

        // Request a fresh keyframe whenever a stream's quality envelope is
        // UPGRADED (more spatial layers requested). On upgrades,
        // the new layer can't be decoded by the receiver until the next
        // keyframe of that layer arrives — periodic keyframes are 3 s apart,
        // so without this we get up to 3 s of stuck-on-old-quality after the
        // upgrade, and the larger render size on the client makes this very
        // visible. On downgrades we skip the request: keeping the higher
        // layer for a bit longer is fine, and burning the per-stream
        // cooldown on a downgrade can block the next upgrade's keyframe.
        // The VideoStreamingBackend.RequestKeyFrame path is throttled (1 s
        // cooldown) so concurrent receivers collapse to one PLI.
        // Small delay so the new envelope propagates before the PLI; otherwise
        // the keyframe can land while some component is still on the old envelope,
        // wasting the anchor and forcing us to wait the full ~3 s periodic interval.
        var upgradedStreams = GetUpgradedStreams(prevState?.QualityByStream, qualityByStream).ToArray();
        if (upgradedStreams.Length != 0) {
            var keyFrameRequests = upgradedStreams
                .Select(x => VideoStreamingBackend.RequestKeyFrame(StreamId.Parse(x), cancellationToken))
                .ToArray();
            await Task.WhenAll(keyFrameRequests).ConfigureAwait(false);
        }
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

        // Dedupe: concurrent viewers on this node coalesce onto a single
        // cross-shard fetch. Without this, N simultaneous joiners would
        // each issue their own GetVideoRaw RPC and N-1 of them would lose
        // the Publish race, wasting RPCs and amplifying join latency.
        var fetched = await RemoteVideoCache
            .EnsureFetched(streamId, FetchAndPublish)
            .ConfigureAwait(false);
        if (!fetched)
            return null;

        var shared = await store.Get(streamId, true, cancellationToken).ConfigureAwait(false);
        return shared?.SkipWhile(f => !f.IsKeyFrame);

        async Task<bool> FetchAndPublish(StreamId sid) {
            // CT.None deliberately: detaches the cached source from this
            // viewer's lifetime so the originating viewer disconnecting
            // cannot tear the cached stream down for siblings. Cache
            // entry's idle expiry handles cleanup of unowned streams.
            var rawRpcStream = await VideoStreamingBackend
                .GetVideoRaw(sid, CancellationToken.None)
                .ConfigureAwait(false);
            if (rawRpcStream == null)
                return false;

            Log.LogInformation("GetOrFetchRemoteVideo: caching #{StreamId} locally", sid);
            // Bounded memoizer: per-layer keyframe-span eviction caps
            // retained content at ~ServerReplayTailDuration per layer,
            // regardless of stream duration. Without this, an 8-hour call
            // would grow the chain to millions of nodes.
            var memoizer = new VideoStreamMemoizer(
                rawRpcStream,
                Constants.Video.ServerReplayTailDuration,
                CancellationToken.None);
            if (!store.Publish(sid, memoizer))
                await memoizer.DisposeAsync().ConfigureAwait(false);
            return true;
        }
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

    private static int MaxLayerIdFromMask(int mask)
        => mask == 0 ? -1 : System.Numerics.BitOperations.Log2((uint)mask);

    private async Task ForwardDemandChanges(
        Session session,
        ApiMap<string, ReceiveQuality>? previous,
        ApiMap<string, ReceiveQuality>? current,
        CancellationToken cancellationToken)
    {
        // Per-stream failures are isolated: a dead publisher node must not
        // break demand reporting for this viewer's other streams.
        var tasks = new List<Task>();
        if (current is not null)
            foreach (var (sid, quality) in current)
                tasks.Add(Report(sid, quality));
        if (previous is not null)
            foreach (var (sid, _) in previous)
                if (current is null || !current.ContainsKey(sid))
                    tasks.Add(Clear(sid));
        if (tasks.Count != 0)
            await Task.WhenAll(tasks).ConfigureAwait(false);
        return;

        async Task Report(string sid, ReceiveQuality quality) {
            try {
                await VideoStreamingBackend
                    .ReportDemand(StreamId.Parse(sid), session.Id, quality, cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (Exception e) when (e is not OperationCanceledException) {
                Log.LogWarning(e, "ReportDemand failed for stream #{StreamId}", sid);
            }
        }

        async Task Clear(string sid) {
            try {
                await VideoStreamingBackend
                    .ClearDemand(StreamId.Parse(sid), session.Id, cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (Exception e) when (e is not OperationCanceledException) {
                Log.LogWarning(e, "ClearDemand failed for stream #{StreamId}", sid);
            }
        }
    }

    internal static IEnumerable<string> GetUpgradedStreams(
        ApiMap<string, ReceiveQuality>? previous,
        ApiMap<string, ReceiveQuality> current)
    {
        foreach (var (streamId, quality) in current) {
            // If there was no explicit prior envelope, or the stream was absent
            // from it, a new non-lowest envelope still needs a fresh anchor.
            // Do not compare against ReceiveQuality.Default here: screencast's
            // top layer id is 1, so that would suppress the PLI needed to
            // switch from L0 to L1.
            var oldQuality = previous is not null && previous.TryGetValue(streamId, out var old)
                ? old
                : ReceiveQuality.Lowest;
            if (quality.LayerId > oldQuality.LayerId)
                yield return streamId;
        }
    }

    private static ApiMap<string, ReceiveQuality> ApplyStreamCountCap(
        ApiMap<string, ReceiveQuality> qualityByStream,
        PlaybackQualityInfo? info)
    {
        const int serverCap = 9;
        var aboveLowest = qualityByStream.Where(kv => !kv.Value.IsLowestOrPaused).ToList();
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
