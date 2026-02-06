using ActualLab.Rpc;

namespace ActualChat.Chat;

/// <summary>
/// Backend service for managing chat threads (reply chains).
/// </summary>
public interface IChatThreadsBackend : IComputeService, IBackendService
{
    Task<AuthorFull?> GetThreadCreator(
        ChatId chatId,
        CancellationToken cancellationToken);
}
