using ActualLab.Rpc;

namespace ActualChat.Chat;

public interface IMentionsBackend : IComputeService, IBackendService
{
    [ComputeMethod]
    Task<Mention?> GetLast(
        ChatId chatId,
        Symbol mentionId,
        CancellationToken cancellationToken);
}
