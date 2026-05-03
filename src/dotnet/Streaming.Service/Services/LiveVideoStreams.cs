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
        TimeSpan skipTo,
        CancellationToken cancellationToken)
    {
        // Stream-level access is gated upstream via List/RegisterMember.
        // skipTo is currently unused — GetVideoRaw already starts at the first
        // available keyframe; the consumer's player advances from there.
        _ = skipTo;
        var streamIdValue = streamId.Value;
        var rawStream = await VideoStreamingBackend.GetVideoRaw(streamId, cancellationToken).ConfigureAwait(false);
        if (rawStream is null)
            return null;

        var filtered = ReceiveQualityFilter.Apply(
            rawStream,
            () => GetReceiveQuality(session, streamIdValue),
            Log,
            cancellationToken);
        return new RpcStream<VideoFrame>(filtered) {
            AllowReconnect = false,
            AckPeriod = Constants.Video.RpcStreamAckPeriod,
            BufferSize = Constants.Video.RpcStreamBufferSize,
        };
    }

    // [ComputeMethod]
    public virtual async Task<VideoQualityPreset> GetQualityPreset(
        Session session,
        StreamId streamId,
        CancellationToken cancellationToken)
        => await VideoStreamingBackend.GetQualityPreset(streamId, cancellationToken).ConfigureAwait(false);

    public async Task PushStream(
        Session session,
        string chatId,
        double sourceStartOffsetSeconds,
        VideoFormat format,
        RpcStream<VideoFrame> frameStream,
        StreamKind streamKind,
        CancellationToken cancellationToken)
    {
        // Live video calls: cap at Constants.Video.MaxLiveDuration (8h) rather than
        // the 3-min chat-entry duration. Every StreamKind (Webcam/Screencast) is a
        // live stream; there is no voice-message-style video path.
        using var stopCts = new CancellationTokenSource(Constants.Video.MaxLiveDuration);
        try {
            var chatIdTyped = ChatId.Parse(chatId);
            var streamId = StreamId.New(MeshWatcher.ThisNode.Ref);
            var videoRecord = new VideoRecord(streamId, session, chatIdTyped, sourceStartOffsetSeconds, format, streamKind);
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
            AppMeters.VideoSendDropRatio.Record(h.SenderFrameDropRatio);
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

        // Buffered media duration ahead of decode is the doc's primary playback
        // health signal and a direct latency proxy — feed app.video.latency.
        if (info is not null) {
            AppMeters.VideoReceiveCapacityBps.Record(info.EstimatedCapacityBytesPerSec);
            AppMeters.VideoReceiveAggregateHealth.Record(info.AggregateHealth);
            foreach (var (_, s) in info.Streams) {
                AppMeters.VideoLatency.Record(s.BufferDurationMsP50);
                if (s.KeyframeSkipsInWindow > 0)
                    AppMeters.VideoReceiveKeyframeSkips.Add(s.KeyframeSkipsInWindow);
                AppMeters.VideoReceiveDecoderQueue.Record(s.DecoderQueueDepthP90);
            }
        }

        var keyFrameRequests = GetLoweredStreams(prevState?.QualityByStream, qualityByStream)
            .Select(x => VideoStreamingBackend.RequestKeyFrame(StreamId.Parse(x), cancellationToken))
            .ToArray();
        if (keyFrameRequests.Length != 0)
            await Task.WhenAll(keyFrameRequests).ConfigureAwait(false);
    }

    // Private methods

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

    private static IEnumerable<string> GetLoweredStreams(
        ApiMap<string, ReceiveQuality>? previous,
        ApiMap<string, ReceiveQuality> current)
    {
        foreach (var (streamId, quality) in current) {
            var oldQuality = previous is not null && previous.TryGetValue(streamId, out var old)
                ? old
                : ReceiveQuality.Default;
            if (quality.MaxSpatialLayer < oldQuality.MaxSpatialLayer
                || quality.MaxTemporalLayer < oldQuality.MaxTemporalLayer)
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

    // Nested types

    private sealed record ReceiveQualityState(
        ApiMap<string, ReceiveQuality> QualityByStream,
        Moment UpdatedAt);
}
