using ActualLab.Rpc;

namespace ActualChat.Chat;

public interface IChatThreadsBackend : IComputeService, IBackendService
{
    Task<AuthorFull?> GetThreadCreator(
        ChatId chatId,
        CancellationToken cancellationToken);
}
