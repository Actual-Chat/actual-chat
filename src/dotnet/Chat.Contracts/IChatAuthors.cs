using ActualChat.Users;

namespace ActualChat.Chat;

public interface IChatAuthors : IComputeService
{
    [ComputeMethod]
    Task<ChatAuthor?> Get(Session session, string chatId, string authorId, CancellationToken cancellationToken);
    [ComputeMethod]
    Task<ChatAuthorFull?> GetOwn(Session session, string chatId, CancellationToken cancellationToken);
    [ComputeMethod]
    Task<ChatAuthorFull?> GetFull(Session session, string chatId, string authorId, CancellationToken cancellationToken);
    [ComputeMethod]
    Task<ImmutableArray<Symbol>> ListChatIds(Session session, CancellationToken cancellationToken);
    [ComputeMethod]
    Task<ImmutableArray<Symbol>> ListAuthorIds(Session session, string chatId, CancellationToken cancellationToken);
    [ComputeMethod]
    Task<ImmutableArray<Symbol>> ListUserIds(Session session, string chatId, CancellationToken cancellationToken);

    [ComputeMethod]
    Task<Presence> GetAuthorPresence(Session session, string chatId, string authorId, CancellationToken cancellationToken);
    [ComputeMethod]
    Task<bool> CanAddToContacts(Session session, string chatId, string authorId, CancellationToken cancellationToken);

    // Commands

    [CommandHandler]
    Task AddToContacts(AddToContactsCommand command, CancellationToken cancellationToken);
    [CommandHandler]
    Task CreateChatAuthors(CreateChatAuthorsCommand command, CancellationToken cancellationToken);
    [CommandHandler]
    Task SetAvatar(SetAvatarCommand command, CancellationToken cancellationToken);

    [DataContract]
    public sealed record AddToContactsCommand(
        [property: DataMember] Session Session,
        [property: DataMember] Symbol ChatId,
        [property: DataMember] Symbol AuthorId
        ) : ISessionCommand<Unit>;

    [DataContract]
    public sealed record CreateChatAuthorsCommand(
        [property: DataMember] Session Session,
        [property: DataMember] Symbol ChatId,
        [property: DataMember] Symbol[] UserIds
        ) : ISessionCommand<Unit>;

    [DataContract]
    public sealed record SetAvatarCommand(
        [property: DataMember] Session Session,
        [property: DataMember] string ChatId,
        [property: DataMember] Symbol AuthorId,
        [property: DataMember] Symbol AvatarId
    ) : ISessionCommand<Unit>;
}
