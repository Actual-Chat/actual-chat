using ActualChat.Attributes;
using ActualChat.Hosting;
using ActualChat.Video;
using ActualLab.Rpc;

namespace ActualChat.Streaming;

[BackendService(nameof(HostRole.VideoBackend), ServiceMode.Distributed)]
[BackendShardScheme(nameof(HostRole.VideoBackend))]
public interface ILiveVideoBackend : IComputeService, IBackendService
{
    [ComputeMethod]
    Task<ApiArray<VideoStreamInfo>> ListActiveStreams(ChatId chatId, CancellationToken cancellationToken);

    [RpcMethod(LocalExecutionMode = RpcLocalExecutionMode.Unconstrained)]
    Task<RpcStream<VideoStreamInfo>> ObserveStreams(ChatId chatId, CancellationToken cancellationToken);

    [ComputeMethod]
    Task<AuthorId[]> GetVideoStreamingAuthorIds(ChatId chatId, CancellationToken cancellationToken);

    [ComputeMethod]
    Task<int> GetVideoStreamMemberCount(ChatId chatId, CancellationToken cancellationToken);

    Task RegisterActiveStream(ChatId chatId, VideoStreamInfo streamInfo, CancellationToken cancellationToken);
    Task UnregisterActiveStream(ChatId chatId, StreamId streamId, CancellationToken cancellationToken);

    Task RegisterVideoStreamMember(ChatId chatId, string sessionId, ApiArray<string> supportedDecoderCodecs, CancellationToken cancellationToken);
    Task UnregisterVideoStreamMember(ChatId chatId, string sessionId, CancellationToken cancellationToken);

    [ComputeMethod]
    Task<string> GetRecommendedCodec(ChatId chatId, CancellationToken cancellationToken);

    [RpcMethod(LocalExecutionMode = RpcLocalExecutionMode.Unconstrained)]
    Task<RpcStream<string>> ObserveRecommendedCodec(ChatId chatId, CancellationToken cancellationToken);
}
