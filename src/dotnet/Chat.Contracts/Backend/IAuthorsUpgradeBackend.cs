namespace ActualChat.Chat;

public interface IAuthorsUpgradeBackend : ICommandService
{
    Task<List<Symbol>> ListChatIds(string userId, CancellationToken cancellationToken);
    Task<List<Symbol>> ListOwnChatIds(Session session, CancellationToken cancellationToken);
}
