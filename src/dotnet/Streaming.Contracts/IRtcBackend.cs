using ActualChat.Attributes;
using ActualChat.Hosting;
using ActualChat.Rtc;
using ActualLab.Rpc;

namespace ActualChat.Streaming;

[BackendService(nameof(HostRole.AudioBackend), ServiceMode.Distributed)]
[BackendShardScheme(nameof(HostRole.AudioBackend))]
public interface IRtcBackend : IComputeService, IBackendService
{
    [ComputeMethod]
    Task<ApiArray<RtcStreamInfo>> ListActiveStreams(ChatId chatId, CancellationToken cancellationToken);

    [RpcMethod(LocalExecutionMode = RpcLocalExecutionMode.Unconstrained)] // Handled internally by that method
    Task<RpcStream<RtcStreamInfo>> ObserveStreams(ChatId chatId, CancellationToken cancellationToken);

    Task RegisterActiveStream(ChatId chatId, RtcStreamInfo activeStream, CancellationToken cancellationToken);
    Task UnregisterActiveStream(ChatId chatId, string streamId, CancellationToken cancellationToken);
}
