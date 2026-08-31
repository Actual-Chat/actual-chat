using ActualChat.Attributes;
using ActualChat.Live;
using ActualLab.Rpc;

namespace ActualChat.Streaming;

[BackendService(nameof(HostRole.LiveBackend), ServiceMode.Distributed)]
[BackendShardScheme(nameof(HostRole.LiveBackend))]
public interface ITypingIndicatorsBackend : IComputeService, IBackendService
{
    [ComputeMethod]
    Task<ApiArray<AuthorId>> ListTypingAuthorIds(ChatId chatId, CancellationToken cancellationToken);

    Task SetTyping(ChatId chatId, AuthorId authorId, TypingKind kind, bool isActive, CancellationToken cancellationToken);
}
