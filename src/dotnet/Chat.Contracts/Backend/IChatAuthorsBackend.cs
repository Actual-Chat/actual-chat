namespace ActualChat.Chat;

public interface IChatAuthorsBackend : IComputeService
{
    [ComputeMethod]
    Task<ChatAuthorFull?> Get(string chatId, string authorId, CancellationToken cancellationToken);
    [ComputeMethod]
    Task<ChatAuthorFull?> GetByUserId(string chatId, string userId, CancellationToken cancellationToken);
    [ComputeMethod]
    Task<ImmutableArray<Symbol>> ListAuthorIds(string chatId, CancellationToken cancellationToken);
    [ComputeMethod]
    Task<ImmutableArray<Symbol>> ListUserIds(string chatId, CancellationToken cancellationToken);
    // TODO(AY): Move this method to IUsersBackend
    [ComputeMethod]
    Task<ImmutableArray<Symbol>> ListUserChatIds(string userId, CancellationToken cancellationToken);

    // Non-compute methods

    Task<ChatAuthorFull> GetOrCreate(Session session, string chatId, CancellationToken cancellationToken);
    Task<ChatAuthorFull> GetOrCreate(string chatId, string userId, CancellationToken cancellationToken);

    // Commands

    [CommandHandler]
    Task<ChatAuthorFull> Create(CreateCommand command, CancellationToken cancellationToken);
    [CommandHandler]
    Task<ChatAuthorFull> ChangeHasLeft(ChangeHasLeftCommand command, CancellationToken cancellationToken);
    [CommandHandler]
    Task<ChatAuthorFull> SetAvatar(SetAvatarCommand command, CancellationToken cancellationToken);

    [DataContract]
    public sealed record CreateCommand(
        [property: DataMember] Symbol ChatId,
        [property: DataMember] Symbol UserId,
        [property: DataMember] bool RequireAccount
        ) : ICommand<ChatAuthorFull>, IBackendCommand;

    [DataContract]
    public sealed record ChangeHasLeftCommand(
        [property: DataMember] Symbol ChatId,
        [property: DataMember] Symbol AuthorId,
        [property: DataMember] bool HasLeft
    ) : ICommand<ChatAuthorFull>, IBackendCommand;

    [DataContract]
    public sealed record SetAvatarCommand(
        [property: DataMember] Symbol ChatId,
        [property: DataMember] Symbol AuthorId,
        [property: DataMember] Symbol AvatarId
    ) : ICommand<ChatAuthorFull>, IBackendCommand;
}
