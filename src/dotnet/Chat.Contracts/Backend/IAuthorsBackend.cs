namespace ActualChat.Chat;

public interface IAuthorsBackend : IComputeService
{
    [ComputeMethod]
    Task<AuthorFull?> Get(ChatId chatId, AuthorId authorId, CancellationToken cancellationToken);
    [ComputeMethod]
    Task<AuthorFull?> GetByUserId(ChatId chatId, UserId userId, CancellationToken cancellationToken);
    [ComputeMethod]
    Task<ImmutableArray<AuthorId>> ListAuthorIds(ChatId chatId, CancellationToken cancellationToken);
    [ComputeMethod]
    Task<ImmutableArray<UserId>> ListUserIds(ChatId chatId, CancellationToken cancellationToken);

    // Commands

    [CommandHandler]
    Task<AuthorFull> Upsert(UpsertCommand command, CancellationToken cancellationToken);
    [CommandHandler]
    Task<AuthorFull> ChangeHasLeft(ChangeHasLeftCommand command, CancellationToken cancellationToken);
    [CommandHandler]
    Task<AuthorFull> SetAvatar(SetAvatarCommand command, CancellationToken cancellationToken);

    [DataContract]
    public sealed record UpsertCommand(
        [property: DataMember] ChatId ChatId,
        [property: DataMember] UserId UserId,
        [property: DataMember] bool? HasLeft
        ) : ICommand<AuthorFull>, IBackendCommand;

    [DataContract]
    public sealed record ChangeHasLeftCommand(
        [property: DataMember] ChatId ChatId,
        [property: DataMember] AuthorId AuthorId,
        [property: DataMember] bool HasLeft
    ) : ICommand<AuthorFull>, IBackendCommand;

    [DataContract]
    public sealed record SetAvatarCommand(
        [property: DataMember] ChatId ChatId,
        [property: DataMember] AuthorId AuthorId,
        [property: DataMember] Symbol AvatarId
    ) : ICommand<AuthorFull>, IBackendCommand;
}
