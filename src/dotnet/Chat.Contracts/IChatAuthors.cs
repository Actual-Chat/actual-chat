using ActualChat.Users;

namespace ActualChat.Chat;

public interface IChatAuthors : IComputeService
{
    [ComputeMethod]
    Task<ChatAuthor?> Get(Session session, string chatId, CancellationToken cancellationToken);
    [ComputeMethod]
    Task<Symbol> GetPrincipalId(Session session, string chatId, CancellationToken cancellationToken);
    [ComputeMethod]
    Task<ImmutableArray<Symbol>> ListChatIds(Session session, CancellationToken cancellationToken);
    [ComputeMethod]
    Task<ImmutableArray<Symbol>> ListAuthorIds(Session session, string chatId, CancellationToken cancellationToken);
    [ComputeMethod]
    Task<ImmutableArray<Symbol>> ListUserIds(Session session, string chatId, CancellationToken cancellationToken);

    [ComputeMethod]
    Task<Author?> GetAuthor(Session session, string chatId, string authorId, bool inherit, CancellationToken cancellationToken);
    [ComputeMethod]
    Task<Presence> GetAuthorPresence(
        Session session,
        string chatId,
        string authorId,
        CancellationToken cancellationToken);
    [ComputeMethod]
    Task<bool> CanAddToContacts(Session session, string chatPrincipalId, CancellationToken cancellationToken);

    // Commands

    [CommandHandler]
    Task AddToContacts(AddToContactsCommand command, CancellationToken cancellationToken);

    [CommandHandler]
    Task CreateChatAuthors(CreateChatAuthorsCommand command, CancellationToken cancellationToken);

    [CommandHandler]
    Task<ChatAuthor> Create(CreateCommand command, CancellationToken cancellationToken);

    [DataContract]
    public sealed record AddToContactsCommand(
        [property: DataMember] Session Session,
        [property: DataMember] string ChatPrincipalId
        ) : ISessionCommand<Unit>;

    [DataContract]
    public sealed record CreateChatAuthorsCommand(
        [property: DataMember] Session Session,
        [property: DataMember] string ChatId,
        [property: DataMember] string[] UserIds
        ) : ISessionCommand<Unit>;

    [DataContract]
    public sealed record CreateCommand(
        [property: DataMember] Session Session,
        [property: DataMember] string ChatId
        ) : ISessionCommand<ChatAuthor>;
}
