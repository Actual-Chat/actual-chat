using ActualChat.Attributes;
using ActualChat.Live;
using ActualLab.Rpc;

namespace ActualChat.Streaming;

[BackendService(nameof(HostRole.LiveBackend), ServiceMode.Distributed)]
[BackendShardScheme(nameof(HostRole.LiveBackend))]
public interface IChatCallReactionsBackend : IComputeService, IBackendService
{
    [ComputeMethod]
    Task<ApiArray<CallReaction>> List(ChatId chatId, CancellationToken cancellationToken);

    Task Send(ChatId chatId, AuthorId authorId, Emoji emoji, CancellationToken cancellationToken);
}
