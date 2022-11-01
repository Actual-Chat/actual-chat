using ActualChat.Users;

namespace ActualChat.Chat;

public interface IAuthors : IComputeService
{
    [ComputeMethod]
    Task<Author?> Get(Session session, string chatId, string authorId, CancellationToken cancellationToken);
    [ComputeMethod]
    Task<AuthorFull?> GetOwn(Session session, string chatId, CancellationToken cancellationToken);
    [ComputeMethod]
    Task<AuthorFull?> GetFull(Session session, string chatId, string authorId, CancellationToken cancellationToken);
    [ComputeMethod]
    Task<ImmutableArray<Symbol>> ListOwnChatIds(Session session, CancellationToken cancellationToken);
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
    Task CreateAuthors(CreateAuthorsCommand command, CancellationToken cancellationToken);
    [CommandHandler]
    Task SetAvatar(SetAvatarCommand command, CancellationToken cancellationToken);

    [DataContract]
    public sealed record AddToContactsCommand(
        [property: DataMember] Session Session,
        [property: DataMember] Symbol ChatId,
        [property: DataMember] Symbol AuthorId
        ) : ISessionCommand<Unit>;

    [DataContract]
    public sealed record CreateAuthorsCommand(
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
