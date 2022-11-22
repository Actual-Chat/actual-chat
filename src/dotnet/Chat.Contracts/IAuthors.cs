using ActualChat.Users;

namespace ActualChat.Chat;

public interface IAuthors : IComputeService
{
    [ComputeMethod]
    Task<Author?> Get(Session session, ChatId chatId, AuthorId authorId, CancellationToken cancellationToken);
    [ComputeMethod]
    Task<AuthorFull?> GetOwn(Session session, ChatId chatId, CancellationToken cancellationToken);
    [ComputeMethod]
    Task<AuthorFull?> GetFull(Session session, ChatId chatId, AuthorId authorId, CancellationToken cancellationToken);
    [ComputeMethod]
    Task<Account?> GetAccount(Session session, ChatId chatId, AuthorId authorId, CancellationToken cancellationToken);

    [ComputeMethod]
    Task<ImmutableArray<AuthorId>> ListAuthorIds(Session session, ChatId chatId, CancellationToken cancellationToken);
    [ComputeMethod]
    Task<ImmutableArray<UserId>> ListUserIds(Session session, ChatId chatId, CancellationToken cancellationToken);

    [ComputeMethod]
    Task<Presence> GetAuthorPresence(Session session, ChatId chatId, AuthorId authorId, CancellationToken cancellationToken);

    // Commands

    [CommandHandler]
    Task CreateAuthors(CreateAuthorsCommand command, CancellationToken cancellationToken);
    [CommandHandler]
    Task SetAvatar(SetAvatarCommand command, CancellationToken cancellationToken);

    [DataContract]
    public sealed record CreateAuthorsCommand(
        [property: DataMember] Session Session,
        [property: DataMember] ChatId ChatId,
        [property: DataMember] UserId[] UserIds
        ) : ISessionCommand<Unit>;

    [DataContract]
    public sealed record SetAvatarCommand(
        [property: DataMember] Session Session,
        [property: DataMember] ChatId ChatId,
        [property: DataMember] Symbol AvatarId
    ) : ISessionCommand<Unit>;
}
