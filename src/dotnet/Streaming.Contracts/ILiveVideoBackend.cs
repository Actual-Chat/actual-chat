using ActualChat.Attributes;
using ActualChat.Hosting;
using ActualLab.Rpc;

namespace ActualChat.Streaming;

[BackendService(nameof(HostRole.LiveBackend), ServiceMode.Distributed)]
[BackendShardScheme(nameof(HostRole.LiveBackend))]
public interface ILiveVideoBackend : IComputeService, IBackendService
{
    [ComputeMethod]
    Task<ApiArray<VideoStreamInfo>> List(ChatId chatId, CancellationToken cancellationToken);

    // NOTE(AY): Do you handle shard set change in this method or its caller? If no, there must be no such method.
    [RpcMethod(LocalExecutionMode = RpcLocalExecutionMode.Unconstrained)]
    Task<RpcStream<VideoStreamInfo>> Observe(ChatId chatId, CancellationToken cancellationToken);

    [ComputeMethod]
    Task<ApiArray<AuthorId>> GetVideoStreamingAuthorIds(ChatId chatId, CancellationToken cancellationToken);

    [ComputeMethod]
    Task<int> GetVideoStreamMemberCount(ChatId chatId, CancellationToken cancellationToken);

    Task Register(ChatId chatId, VideoStreamInfo streamInfo, CancellationToken cancellationToken);
    Task Unregister(ChatId chatId, StreamId streamId, CancellationToken cancellationToken);

    Task RegisterMember(ChatId chatId, string sessionId, ApiArray<string> supportedDecoderCodecs, CancellationToken cancellationToken);
    Task UnregisterMember(ChatId chatId, string sessionId, CancellationToken cancellationToken);

    [ComputeMethod]
    Task<ApiArray<string>> GetSupportedDecoderCodecs(ChatId chatId, CancellationToken cancellationToken);

    // NOTE(AY): Do you handle shard set change in this method or its caller? If no, there must be no such method.
    [RpcMethod(LocalExecutionMode = RpcLocalExecutionMode.Unconstrained)]
    Task<RpcStream<ApiArray<string>>> ObserveSupportedDecoderCodecs(ChatId chatId, CancellationToken cancellationToken);
}
