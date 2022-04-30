using ActualChat.Users;

namespace ActualChat.Chat;

public interface IChatAuthors
{
    [ComputeMethod(KeepAliveTime = 10)]
    Task<ChatAuthor?> GetChatAuthor(Session session, string chatId, CancellationToken cancellationToken);
    [ComputeMethod(KeepAliveTime = 10)]
    Task<string> GetChatPrincipalId(Session session, string chatId, CancellationToken cancellationToken);
    [ComputeMethod(KeepAliveTime = 10)]
    Task<Author?> GetAuthor(string chatId, string authorId, bool inherit, CancellationToken cancellationToken);
    [ComputeMethod(KeepAliveTime = 10)]
    Task<Presence> GetAuthorPresence(string chatId, string authorId, CancellationToken cancellationToken);
    [ComputeMethod(KeepAliveTime = 10)]
    Task<string[]> GetChatIds(Session session, CancellationToken cancellationToken);
    [ComputeMethod(KeepAliveTime = 1)]
    Task<string?> GetChatAuthorAvatarId(Session session, string chatId, CancellationToken cancellationToken);
    [ComputeMethod(KeepAliveTime = 1)]
    Task<bool> CanAddToContacts(Session session, string chatAuthorId, CancellationToken cancellationToken);
    [ComputeMethod(KeepAliveTime = 10)]
    Task<ImmutableArray<string>> GetAuthorIds(Session session, string chatId, CancellationToken cancellationToken);

    // Commands

    [CommandHandler]
    Task AddToContacts(AddToContactsCommand command, CancellationToken cancellationToken);

    public record AddToContactsCommand(Session Session, string ChatAuthorId) : ISessionCommand<Unit>;
}
