using ActualLab.Rpc;

namespace ActualChat.Streaming;

public interface IRealtimeStreaming : IComputeService
{
    // State queries (ComputeMethods - auto-invalidate on change)
    [ComputeMethod]
    Task<ActiveVideoStreams> GetActiveVideoStreams(Session session, ChatId chatId, CancellationToken cancellationToken);

    [ComputeMethod]
    Task<VideoStreamInfo?> GetVideoStreamInfo(Session session, StreamId streamId, CancellationToken cancellationToken);

    [ComputeMethod]
    Task<AuthorId[]> GetVideoStreamingAuthorIds(Session session, ChatId chatId, CancellationToken cancellationToken);

    [ComputeMethod]
    Task<bool> IsAuthorVideoStreaming(Session session, ChatId chatId, AuthorId authorId, CancellationToken cancellationToken);

    // Viewer count
    [ComputeMethod]
    Task<int> GetVideoStreamMemberCount(Session session, ChatId chatId, CancellationToken cancellationToken);

    // Viewer registration (non-compute)
    Task RegisterVideoStreamMember(Session session, ChatId chatId, CancellationToken cancellationToken);
    Task UnregisterVideoStreamMember(Session session, ChatId chatId, CancellationToken cancellationToken);

    // Real-time event subscription (RpcStream - push events)
    Task<RpcStream<VideoStreamEvent>> SubscribeToVideoStreamEvents(
        Session session, ChatId chatId, CancellationToken cancellationToken);
}
