namespace ActualChat.Chat;

public interface IAuthorsUpgradeBackend : ICommandService
{
    Task<List<ChatId>> ListChatIds(UserId userId, CancellationToken cancellationToken);
    Task<List<ChatId>> ListOwnChatIds(Session session, CancellationToken cancellationToken);
}
