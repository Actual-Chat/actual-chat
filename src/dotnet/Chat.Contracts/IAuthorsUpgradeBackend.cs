using ActualLab.Rpc;

namespace ActualChat.Chat;

public interface IAuthorsUpgradeBackend : ICommandService, IBackendService
{
    Task<List<ChatId>> ListChatIds(UserId userId, CancellationToken cancellationToken);
    Task<List<ChatId>> ListOwnChatIds(Session session, CancellationToken cancellationToken);
}
