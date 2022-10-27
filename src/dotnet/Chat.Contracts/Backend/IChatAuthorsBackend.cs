namespace ActualChat.Chat;

public interface IChatAuthorsBackend : IComputeService
{
    [ComputeMethod]
    Task<ChatAuthorFull?> Get(string chatId, string authorId, bool inherit, CancellationToken cancellationToken);
    [ComputeMethod]
    Task<ChatAuthorFull?> GetByUserId(string chatId, string userId, bool inherit, CancellationToken cancellationToken);
    [ComputeMethod]
    Task<ImmutableArray<Symbol>> ListAuthorIds(string chatId, CancellationToken cancellationToken);
    [ComputeMethod]
    Task<ImmutableArray<Symbol>> ListUserIds(string chatId, CancellationToken cancellationToken);
    // TODO(AY): Move this method to IUsersBackend
    [ComputeMethod]
    Task<ImmutableArray<Symbol>> ListUserChatIds(string userId, CancellationToken cancellationToken);

    // Non-compute methods

    Task<ChatAuthor> GetOrCreate(Session session, string chatId, CancellationToken cancellationToken);
    Task<ChatAuthor> GetOrCreate(string chatId, string userId, bool inherit, CancellationToken cancellationToken);

    // Commands

    [CommandHandler]
    Task<ChatAuthorFull> Create(CreateCommand command, CancellationToken cancellationToken);
    [CommandHandler]
    Task<ChatAuthorFull> ChangeHasLeft(ChangeHasLeftCommand command, CancellationToken cancellationToken);

    [DataContract]
    public sealed record CreateCommand(
        [property: DataMember] string ChatId,
        [property: DataMember] string UserId,
        [property: DataMember] bool RequireAccount
        ) : ICommand<ChatAuthorFull>, IBackendCommand;

    [DataContract]
    public sealed record ChangeHasLeftCommand(
        [property: DataMember] string AuthorId,
        [property: DataMember] bool HasLeft
    ) : ICommand<ChatAuthorFull>, IBackendCommand;
}
