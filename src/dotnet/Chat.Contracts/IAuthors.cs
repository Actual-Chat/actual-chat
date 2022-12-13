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
    Task<Presence> GetPresence(Session session, ChatId chatId, AuthorId authorId, CancellationToken cancellationToken);

    [ComputeMethod]
    Task<ImmutableArray<AuthorId>> ListAuthorIds(Session session, ChatId chatId, CancellationToken cancellationToken);
    [ComputeMethod]
    Task<ImmutableArray<UserId>> ListUserIds(Session session, ChatId chatId, CancellationToken cancellationToken);

    // Commands

    [CommandHandler]
    Task<AuthorFull> Join(JoinCommand command, CancellationToken cancellationToken);
    [CommandHandler]
    Task Leave(LeaveCommand command, CancellationToken cancellationToken);
    [CommandHandler]
    Task Invite(InviteCommand command, CancellationToken cancellationToken);
    [CommandHandler]
    Task SetAvatar(SetAvatarCommand command, CancellationToken cancellationToken);

    [DataContract]
    public sealed record JoinCommand(
        [property: DataMember] Session Session,
        [property: DataMember] ChatId ChatId
    ) : ISessionCommand<AuthorFull>;

    [DataContract]
    public sealed record LeaveCommand(
        [property: DataMember] Session Session,
        [property: DataMember] ChatId ChatId
    ) : ISessionCommand<Unit>;

    [DataContract]
    public sealed record InviteCommand(
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
