using ActualChat.Attributes;
using ActualChat.Hosting;
using ActualChat.Live;
using ActualLab.Rpc;

namespace ActualChat.Streaming;

/// <summary>
/// Backend service for managing live audio streams in chats.
/// </summary>
[BackendService(nameof(HostRole.AudioBackend), ServiceMode.Distributed)]
[BackendShardScheme(nameof(HostRole.AudioBackend))]
public interface ILiveBackend : IComputeService, IBackendService
{
    [ComputeMethod]
    Task<ApiArray<LiveStreamInfo>> ListActiveStreams(ChatId chatId, CancellationToken cancellationToken);

    [RpcMethod(LocalExecutionMode = RpcLocalExecutionMode.Unconstrained)] // Handled internally by that method
    Task<RpcStream<LiveStreamInfo>> ObserveStreams(ChatId chatId, CancellationToken cancellationToken);

    Task RegisterActiveStream(ChatId chatId, LiveStreamInfo activeStream, CancellationToken cancellationToken);
    Task UnregisterActiveStream(ChatId chatId, string streamId, CancellationToken cancellationToken);
}
