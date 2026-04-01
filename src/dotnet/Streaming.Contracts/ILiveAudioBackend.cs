using ActualChat.Attributes;
using ActualChat.Hosting;
using ActualChat.Live;
using ActualLab.Rpc;

namespace ActualChat.Streaming;

[BackendService(nameof(HostRole.LiveBackend), ServiceMode.Distributed)]
[BackendShardScheme(nameof(HostRole.LiveBackend))]
public interface ILiveAudioBackend : IComputeService, IBackendService
{
    [ComputeMethod]
    Task<ApiArray<LiveStreamInfo>> List(ChatId chatId, CancellationToken cancellationToken);

    Task Register(ChatId chatId, LiveStreamInfo streamInfo, CancellationToken cancellationToken);
    Task Unregister(ChatId chatId, string streamId, CancellationToken cancellationToken);
}
