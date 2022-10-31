namespace ActualChat.Chat;

public interface IAuthorsBackend : IComputeService
{
    [ComputeMethod]
    Task<AuthorFull?> Get(string chatId, string authorId, CancellationToken cancellationToken);
    [ComputeMethod]
    Task<AuthorFull?> GetByUserId(string chatId, string userId, CancellationToken cancellationToken);
    [ComputeMethod]
    Task<ImmutableArray<Symbol>> ListAuthorIds(string chatId, CancellationToken cancellationToken);
    [ComputeMethod]
    Task<ImmutableArray<Symbol>> ListUserIds(string chatId, CancellationToken cancellationToken);
    // TODO(AY): Move this method to IUsersBackend
    [ComputeMethod]
    Task<ImmutableArray<Symbol>> ListUserChatIds(string userId, CancellationToken cancellationToken);

    // Non-compute methods

    Task<AuthorFull> GetOrCreate(Session session, string chatId, CancellationToken cancellationToken);
    Task<AuthorFull> GetOrCreate(string chatId, string userId, CancellationToken cancellationToken);

    // Commands

    [CommandHandler]
    Task<AuthorFull> Create(CreateCommand command, CancellationToken cancellationToken);
    [CommandHandler]
    Task<AuthorFull> ChangeHasLeft(ChangeHasLeftCommand command, CancellationToken cancellationToken);
    [CommandHandler]
    Task<AuthorFull> SetAvatar(SetAvatarCommand command, CancellationToken cancellationToken);

    [DataContract]
    public sealed record CreateCommand(
        [property: DataMember] Symbol ChatId,
        [property: DataMember] Symbol UserId,
        [property: DataMember] bool RequireAccount
        ) : ICommand<AuthorFull>, IBackendCommand;

    [DataContract]
    public sealed record ChangeHasLeftCommand(
        [property: DataMember] Symbol ChatId,
        [property: DataMember] Symbol AuthorId,
        [property: DataMember] bool HasLeft
    ) : ICommand<AuthorFull>, IBackendCommand;

    [DataContract]
    public sealed record SetAvatarCommand(
        [property: DataMember] Symbol ChatId,
        [property: DataMember] Symbol AuthorId,
        [property: DataMember] Symbol AvatarId
    ) : ICommand<AuthorFull>, IBackendCommand;
}
