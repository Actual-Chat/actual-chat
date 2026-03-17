using ActualChat.Chat;
using ActualChat.Video;
using ActualLab.Rpc;
using ActualLab.Rpc.Infrastructure;

namespace ActualChat.Streaming;

public class LiveVideoStreams(IServiceProvider services) : ILiveVideoStreams
{
    private ILiveVideoBackend Backend { get; } = services.GetRequiredService<ILiveVideoBackend>();
    private IVideoStreamingBackend VideoStreamingBackend { get; } = services.GetRequiredService<IVideoStreamingBackend>();
    private IChats Chats { get; } = services.GetRequiredService<IChats>();
    private ILogger Log => field ??= services.LogFor(GetType());

    // [ComputeMethod]
    public virtual async Task<ApiArray<VideoStreamInfo>> ListActiveStreams(
        Session session,
        ChatId chatId,
        CancellationToken cancellationToken)
    {
        var chatRules = await Chats.GetRules(session, chatId, cancellationToken).ConfigureAwait(false);
        chatRules.Require(ChatPermissions.Read);
        var result = await Backend.ListActiveStreams(chatId, cancellationToken).ConfigureAwait(false);
        Log.LogWarning("ListActiveStreams(session, {ChatId}): returning {Count} streams", chatId, result.Count);
        return result;
    }

    // [ComputeMethod]
    public virtual async Task<ApiArray<AuthorId>> GetVideoStreamingAuthorIds(
        Session session,
        ChatId chatId,
        CancellationToken cancellationToken)
    {
        var chatRules = await Chats.GetRules(session, chatId, cancellationToken).ConfigureAwait(false);
        chatRules.Require(ChatPermissions.Read);
        var result = await Backend.GetVideoStreamingAuthorIds(chatId, cancellationToken).ConfigureAwait(false);
        Log.LogWarning("GetVideoStreamingAuthorIds(session, {ChatId}): returning {Count} authors", chatId, result.Count);
        return result;
    }

    // [ComputeMethod]
    public virtual async Task<int> GetVideoStreamMemberCount(
        Session session,
        ChatId chatId,
        CancellationToken cancellationToken)
    {
        var chatRules = await Chats.GetRules(session, chatId, cancellationToken).ConfigureAwait(false);
        chatRules.Require(ChatPermissions.Read);
        return await Backend.GetVideoStreamMemberCount(chatId, cancellationToken).ConfigureAwait(false);
    }

    public async Task RegisterVideoStreamMember(
        Session session,
        ChatId chatId,
        ApiArray<string> supportedDecoderCodecs,
        CancellationToken cancellationToken)
    {
        var chatRules = await Chats.GetRules(session, chatId, cancellationToken).ConfigureAwait(false);
        chatRules.Require(ChatPermissions.Read);
        await Backend.RegisterVideoStreamMember(chatId, session.Id, supportedDecoderCodecs, cancellationToken).ConfigureAwait(false);
    }

    public async Task UnregisterVideoStreamMember(
        Session session,
        ChatId chatId,
        CancellationToken cancellationToken)
    {
        var chatRules = await Chats.GetRules(session, chatId, cancellationToken).ConfigureAwait(false);
        chatRules.Require(ChatPermissions.Read);
        await Backend.UnregisterVideoStreamMember(chatId, session.Id, cancellationToken).ConfigureAwait(false);
    }

    // [ComputeMethod]
    public virtual async Task<ApiArray<string>> GetSupportedDecoderCodecs(
        Session session,
        ChatId chatId,
        CancellationToken cancellationToken)
    {
        var chatRules = await Chats.GetRules(session, chatId, cancellationToken).ConfigureAwait(false);
        chatRules.Require(ChatPermissions.Read);
        return await Backend.GetSupportedDecoderCodecs(chatId, cancellationToken).ConfigureAwait(false);
    }

    public async Task<RpcStream<ApiArray<string>>> ObserveSupportedDecoderCodecs(
        Session session,
        ChatId chatId,
        CancellationToken cancellationToken)
    {
        var chatRules = await Chats.GetRules(session, chatId, cancellationToken).ConfigureAwait(false);
        chatRules.Require(ChatPermissions.Read);
        var remoteStream = await Backend.ObserveSupportedDecoderCodecs(chatId, cancellationToken).ConfigureAwait(false);
        return RpcStream.New(remoteStream); // Wrap Remote → Local for re-serialization
    }

    public async Task<RpcStream<VideoFrame>?> GetVideo(
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

    public async Task<RpcStream<VideoQualityPreset>> ObserveStreamQualityRequests(
        Session session,
        StreamId streamId,
        CancellationToken cancellationToken)
    {
        var remoteStream = await VideoStreamingBackend.ObserveStreamQualityRequests(streamId, cancellationToken).ConfigureAwait(false);
        return RpcStream.New(remoteStream);
    }
}
