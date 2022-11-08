namespace ActualChat.Chat;

public interface IAuthorsUpgradeBackend : ICommandService
{
    Task<ImmutableArray<Symbol>> ListOwnChatIds(Session session, CancellationToken cancellationToken);
}
