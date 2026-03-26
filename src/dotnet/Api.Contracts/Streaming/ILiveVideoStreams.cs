using ActualChat.Video;
using ActualLab.Rpc;

namespace ActualChat.Streaming;

public interface ILiveVideoStreams : IComputeService
{
    [ComputeMethod]
    Task<ApiArray<VideoStreamInfo>> List(Session session, ChatId chatId, CancellationToken cancellationToken);
    [ComputeMethod]
    Task<ApiArray<AuthorId>> GetAuthorIds(Session session, ChatId chatId, CancellationToken cancellationToken);
    // NOTE(AY): Why/how can it differ from what GetAuthorIds returns?
    [ComputeMethod]
    Task<int> GetMemberCount(Session session, ChatId chatId, CancellationToken cancellationToken);

    Task RegisterMember(Session session, ChatId chatId, ApiArray<string> supportedDecoderCodecs, CancellationToken cancellationToken);
    Task UnregisterMember(Session session, ChatId chatId, CancellationToken cancellationToken);

    [ComputeMethod]
    Task<ApiArray<string>> GetSupportedDecoderCodecs(Session session, ChatId chatId, CancellationToken cancellationToken);
    [RpcMethod(LocalExecutionMode = RpcLocalExecutionMode.Unconstrained)]
    Task<RpcStream<ApiArray<string>>> ObserveSupportedDecoderCodecs(Session session, ChatId chatId, CancellationToken cancellationToken);

    Task<RpcStream<VideoFrame>?> GetStream(Session session, StreamId streamId, TimeSpan skipTo, CancellationToken cancellationToken);
    // NOTE(AY): [RpcMethod(LocalExecutionMode = RpcLocalExecutionMode.Unconstrained)]?
    Task<RpcStream<VideoQualityPreset>> ObserveQualityRequests(Session session, StreamId streamId, CancellationToken cancellationToken);
}
