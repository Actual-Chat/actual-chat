using ActualChat.Video;
using ActualLab.Rpc;
using ActualLab.Rpc.Infrastructure;

namespace ActualChat.Streaming.Services;

public class LiveVideoStreams(IServiceProvider services) : ILiveVideoStreams
{
    private ILiveVideoBackend Backend { get; } = services.GetRequiredService<ILiveVideoBackend>();
    private IVideoStreamingBackend VideoStreamingBackend { get; } = services.GetRequiredService<IVideoStreamingBackend>();
    private IChats Chats { get; } = services.GetRequiredService<IChats>();
    private ILogger Log => field ??= services.LogFor(GetType());

    // [ComputeMethod]
    public virtual async Task<ApiArray<VideoStreamInfo>> List(
        Session session,
        ChatId chatId,
        CancellationToken cancellationToken)
    {
        var chatRules = await Chats.GetRules(session, chatId, cancellationToken).ConfigureAwait(false);
        chatRules.Require(ChatPermissions.Read);
        var result = await Backend.List(chatId, cancellationToken).ConfigureAwait(false);
        Log.LogWarning("ListActiveStreams(session, {ChatId}): returning {Count} streams", chatId, result.Count);
        return result;
    }

    // [ComputeMethod]
    public virtual async Task<int> GetMemberCount(
        Session session,
        ChatId chatId,
        CancellationToken cancellationToken)
    {
        var chatRules = await Chats.GetRules(session, chatId, cancellationToken).ConfigureAwait(false);
        chatRules.Require(ChatPermissions.Read);
        return await Backend.GetVideoStreamMemberCount(chatId, cancellationToken).ConfigureAwait(false);
    }

    public async Task RegisterMember(
        Session session,
        ChatId chatId,
        ApiArray<string> supportedDecoderCodecs,
        CancellationToken cancellationToken)
    {
        var chatRules = await Chats.GetRules(session, chatId, cancellationToken).ConfigureAwait(false);
        chatRules.Require(ChatPermissions.Read);
        await Backend.RegisterMember(chatId, session.Id, supportedDecoderCodecs, cancellationToken).ConfigureAwait(false);
    }

    public async Task UnregisterMember(
        Session session,
        ChatId chatId,
        CancellationToken cancellationToken)
    {
        var chatRules = await Chats.GetRules(session, chatId, cancellationToken).ConfigureAwait(false);
        chatRules.Require(ChatPermissions.Read);
        await Backend.UnregisterMember(chatId, session.Id, cancellationToken).ConfigureAwait(false);
    }

    // [ComputeMethod]
    public virtual async Task<ApiArray<string>> GetSupportedCodecs(
        Session session,
        ChatId chatId,
        CancellationToken cancellationToken)
    {
        var chatRules = await Chats.GetRules(session, chatId, cancellationToken).ConfigureAwait(false);
        chatRules.Require(ChatPermissions.Read);
        return await Backend.GetSupportedCodecs(chatId, cancellationToken).ConfigureAwait(false);
    }

    public async Task<RpcStream<VideoFrame>?> GetStream(
        Session session,
        StreamId streamId,
        TimeSpan skipTo,
        CancellationToken cancellationToken)
    {
        var peerId = RpcInboundContext.Current?.Peer.Id.ToString() ?? "rpc-unknown";
        var remoteStream = await VideoStreamingBackend.GetVideo(streamId, skipTo, peerId, cancellationToken).ConfigureAwait(false);
        return remoteStream is null
            ? null
            : RpcStream.New(remoteStream, allowReconnect: false);
    }

    // [ComputeMethod]
    public virtual async Task<bool> HasActiveScreencast(
        Session session,
        ChatId chatId,
        CancellationToken cancellationToken)
    {
        var chatRules = await Chats.GetRules(session, chatId, cancellationToken).ConfigureAwait(false);
        chatRules.Require(ChatPermissions.Read);
        return await Backend.HasActiveScreencast(chatId, cancellationToken).ConfigureAwait(false);
    }

    // [ComputeMethod]
    public virtual async Task<VideoQualityPreset> GetQualityPreset(
        Session session,
        StreamId streamId,
        CancellationToken cancellationToken)
        => await VideoStreamingBackend.GetQualityPreset(streamId, cancellationToken).ConfigureAwait(false);

    // Legacy v2.6 compatibility methods

#pragma warning disable CS0618 // Type or member is obsolete

    // [ComputeMethod]
    public virtual async Task<ApiArray<AuthorId>> LegacyGetVideoStreamingAuthorIds(
        Session session,
        ChatId chatId,
        CancellationToken cancellationToken)
    {
        var streams = await List(session, chatId, cancellationToken).ConfigureAwait(false);
        return streams.Select(s => s.AuthorId).Distinct().ToApiArray();
    }

    public async Task<RpcStream<ApiArray<string>>> LegacyObserveSupportedDecoderCodecs(
        Session session,
        ChatId chatId,
        CancellationToken cancellationToken)
    {
        var codecs = await GetSupportedCodecs(session, chatId, cancellationToken).ConfigureAwait(false);
        return RpcStream.New(new[] { codecs }.ToAsyncEnumerable());
    }

    public async Task<RpcStream<VideoQualityPreset>> LegacyObserveStreamQualityRequests(
        Session session,
        StreamId streamId,
        CancellationToken cancellationToken)
    {
        var preset = await GetQualityPreset(session, streamId, cancellationToken).ConfigureAwait(false);
        return RpcStream.New(new[] { preset }.ToAsyncEnumerable());
    }

#pragma warning restore CS0618
}
