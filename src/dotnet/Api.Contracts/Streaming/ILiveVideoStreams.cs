using ActualChat.Video;
using ActualLab.Rpc;

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

    Task<RpcStream<VideoFrame>?> GetVideo(Session session, StreamId streamId, TimeSpan skipTo, CancellationToken cancellationToken);
    Task<RpcStream<VideoQualityPreset>> ObserveStreamQualityRequests(Session session, StreamId streamId, CancellationToken cancellationToken);
}
