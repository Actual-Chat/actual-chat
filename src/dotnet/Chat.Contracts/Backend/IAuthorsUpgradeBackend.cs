namespace ActualChat.Chat;

public interface IAuthorsUpgradeBackend
{
    Task<ImmutableArray<Symbol>> ListOwnChatIds(Session session, CancellationToken cancellationToken);
}
