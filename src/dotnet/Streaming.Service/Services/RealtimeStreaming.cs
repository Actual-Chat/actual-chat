using ActualChat.Chat;
using ActualLab.Rpc;

namespace ActualChat.Streaming;

public class RealtimeStreaming(IServiceProvider services) : IRealtimeStreaming
{
    private IRealtimeStreamingBackend Backend { get; } = services.GetRequiredService<IRealtimeStreamingBackend>();
    private IChats Chats { get; } = services.GetRequiredService<IChats>();

    // [ComputeMethod]
    public virtual async Task<ActiveVideoStreams> GetActiveVideoStreams(
        Session session,
        ChatId chatId,
        CancellationToken cancellationToken)
    {
        var chatRules = await Chats.GetRules(session, chatId, cancellationToken).ConfigureAwait(false);
        chatRules.Require(ChatPermissions.Read);
        return await Backend.GetActiveVideoStreams(chatId, cancellationToken).ConfigureAwait(false);
    }

    // [ComputeMethod]
    public virtual async Task<VideoStreamInfo?> GetVideoStreamInfo(
        Session session,
        StreamId streamId,
        CancellationToken cancellationToken)
    {
        var streamInfo = await Backend.GetVideoStreamInfo(streamId, cancellationToken).ConfigureAwait(false);
        if (streamInfo == null)
            return null;

        var chatRules = await Chats.GetRules(session, streamInfo.ChatId, cancellationToken).ConfigureAwait(false);
        chatRules.Require(ChatPermissions.Read);
        return streamInfo;
    }

    // [ComputeMethod]
    public virtual async Task<AuthorId[]> GetVideoStreamingAuthorIds(
        Session session,
        ChatId chatId,
        CancellationToken cancellationToken)
    {
        var chatRules = await Chats.GetRules(session, chatId, cancellationToken).ConfigureAwait(false);
        chatRules.Require(ChatPermissions.Read);
        return await Backend.GetVideoStreamingAuthorIds(chatId, cancellationToken).ConfigureAwait(false);
    }

    // [ComputeMethod]
    public virtual async Task<bool> IsAuthorVideoStreaming(
        Session session,
        ChatId chatId,
        AuthorId authorId,
        CancellationToken cancellationToken)
    {
        var chatRules = await Chats.GetRules(session, chatId, cancellationToken).ConfigureAwait(false);
        chatRules.Require(ChatPermissions.Read);
        return await Backend.IsAuthorVideoStreaming(chatId, authorId, cancellationToken).ConfigureAwait(false);
    }

    public async Task<RpcStream<VideoStreamEvent>> SubscribeToVideoStreamEvents(
        Session session,
        ChatId chatId,
        CancellationToken cancellationToken)
    {
        var chatRules = await Chats.GetRules(session, chatId, cancellationToken).ConfigureAwait(false);
        chatRules.Require(ChatPermissions.Read);
        return await Backend.SubscribeToVideoStreamEvents(chatId, cancellationToken).ConfigureAwait(false);
    }
}
