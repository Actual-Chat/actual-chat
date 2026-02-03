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

    Task<RpcStream<RtcStreamInfo>> ObserveNewStreams(ChatId chatId, CancellationToken cancellationToken);
}
