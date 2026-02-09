namespace ActualChat.Streaming;

public interface ILiveVideoStreams : IComputeService
{
    [ComputeMethod]
    Task<ApiArray<VideoStreamInfo>> ListActiveStreams(Session session, ChatId chatId, CancellationToken cancellationToken);

    [ComputeMethod]
    Task<AuthorId[]> GetVideoStreamingAuthorIds(Session session, ChatId chatId, CancellationToken cancellationToken);

    [ComputeMethod]
    Task<int> GetVideoStreamMemberCount(Session session, ChatId chatId, CancellationToken cancellationToken);

    Task RegisterVideoStreamMember(Session session, ChatId chatId, CancellationToken cancellationToken);
    Task UnregisterVideoStreamMember(Session session, ChatId chatId, CancellationToken cancellationToken);
}
