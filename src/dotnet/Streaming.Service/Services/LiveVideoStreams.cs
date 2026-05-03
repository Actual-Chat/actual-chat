using ActualChat.Video;
using ActualLab.Rpc;

namespace ActualChat.Streaming.Services;

public class LiveVideoStreams(IServiceProvider services) : ILiveVideoStreams
{
    private MeshWatcher MeshWatcher { get; } = services.MeshWatcher();
    private ILiveVideoBackend Backend { get; } = services.GetRequiredService<ILiveVideoBackend>();
    private IVideoStreamingBackend VideoStreamingBackend { get; } = services.GetRequiredService<IVideoStreamingBackend>();
    private IChats Chats { get; } = services.GetRequiredService<IChats>();
    private ILogger Log => field ??= services.LogFor(GetType());

    // Per-session per-stream ReceiveQuality. Replaced atomically by
    // ChangePlaybackQuality. Sticky routing keeps state node-local.
    private readonly ConcurrentDictionary<Session, ApiMap<string, ReceiveQuality>> _receiveQuality = new();

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

    public Task<RpcNoWait> RequestKeyFrame(Session session, string streamId, CancellationToken cancellationToken)
    {
        _ = session;
        var sid = StreamId.Parse(streamId);
        return RpcNoWait.Tasks.From(VideoStreamingBackend.RequestKeyFrame(sid, cancellationToken));
    }

    public Task<RpcNoWait> ChangeRecordingQuality(
        Session session,
        RecordingQualityState? state,
        RecordingQualityInfo? info,
        CancellationToken cancellationToken)
    {
        _ = cancellationToken;
        Log.LogTrace("ChangeRecordingQuality: session={Session}, state={State}, info={Info}",
            session, state, info);
        return RpcNoWait.Tasks.Completed;
    }

    public Task<RpcNoWait> ChangePlaybackQuality(
        Session session,
        ApiMap<string, ReceiveQuality>? requestedQuality,
        PlaybackQualityInfo? info,
        CancellationToken cancellationToken)
    {
        if (requestedQuality is null) {
            Log.LogTrace("ChangePlaybackQuality: session={Session}, info={Info} (no-op)", session, info);
            return RpcNoWait.Tasks.Completed;
        }

        var capped = ApplyServerCap(requestedQuality, info);
        _receiveQuality.TryGetValue(session, out var previous);
        _receiveQuality[session] = capped;
        Log.LogTrace("ChangePlaybackQuality: session={Session}, streams={Count}, info={Info}",
            session, capped.Count, info);
        var keyFrameRequests = GetLoweredStreams(previous, capped)
            .Select(x => VideoStreamingBackend.RequestKeyFrame(StreamId.Parse(x), cancellationToken))
            .ToArray();
        return keyFrameRequests.Length == 0
            ? RpcNoWait.Tasks.Completed
            : RpcNoWait.Tasks.From(Task.WhenAll(keyFrameRequests));
    }

    // Private methods

    private ReceiveQuality GetReceiveQuality(Session session, string streamId)
    {
        if (!_receiveQuality.TryGetValue(session, out var map))
            return ReceiveQuality.Default;
        return map.TryGetValue(streamId, out var quality) ? quality : ReceiveQuality.Lowest;
    }

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

    private static ApiMap<string, ReceiveQuality> ApplyServerCap(
        ApiMap<string, ReceiveQuality> requested,
        PlaybackQualityInfo? info)
    {
        const int serverCap = 9;
        var aboveLowest = new List<KeyValuePair<string, ReceiveQuality>>();
        foreach (var kv in requested) {
            if (!kv.Value.IsLowest)
                aboveLowest.Add(kv);
        }
        if (aboveLowest.Count <= serverCap)
            return requested;

        // Demote secondaries before primaries; preserve request order otherwise.
        // info.Streams[streamId].Priority cross-reference falls back to Secondary.
        int PriorityRank(string streamId)
        {
            if (info is null || !info.Streams.TryGetValue(streamId, out var streamInfo))
                return 0; // Secondary
            return streamInfo.Priority == PlaybackStreamPriority.Primary ? 1 : 0;
        }
        var ordered = aboveLowest
            .Select((kv, idx) => (kv, rank: PriorityRank(kv.Key), idx))
            .OrderBy(x => x.rank)
            .ThenBy(x => x.idx)
            .ToList();

        var demoteCount = aboveLowest.Count - serverCap;
        var demoted = new HashSet<string>();
        for (var i = 0; i < demoteCount; i++)
            demoted.Add(ordered[i].kv.Key);

        var result = new ApiMap<string, ReceiveQuality>();
        foreach (var kv in requested)
            result[kv.Key] = demoted.Contains(kv.Key) ? ReceiveQuality.Lowest : kv.Value;
        return result;
    }
}
