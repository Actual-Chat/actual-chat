namespace ActualChat.Chat;

public interface IChatAuthorsBackend : IComputeService
{
    [ComputeMethod]
    Task<ChatAuthor?> Get(string chatId, string authorId, bool inherit, CancellationToken cancellationToken);
    [ComputeMethod]
    Task<ChatAuthor?> GetByUserId(string chatId, string userId, bool inherit, CancellationToken cancellationToken);
    [ComputeMethod]
    Task<ImmutableArray<Symbol>> ListAuthorIds(string chatId, CancellationToken cancellationToken);
    [ComputeMethod]
    Task<ImmutableArray<Symbol>> ListUserIds(string chatId, CancellationToken cancellationToken);
    // TODO(AY): Move this method to IUsersBackend
    [ComputeMethod]
    Task<ImmutableArray<Symbol>> ListUserChatIds(string userId, CancellationToken cancellationToken);

    // Non-compute methods
    Task<ChatAuthor> GetOrCreate(string chatId, string userId, bool inherit, CancellationToken cancellationToken);

    // Commands

    [CommandHandler]
    Task<ChatAuthor> Create(CreateCommand command, CancellationToken cancellationToken);
    [CommandHandler]
    Task<ChatAuthor> ChangeHasLeft(ChangeHasLeftCommand command, CancellationToken cancellationToken);

    [DataContract]
    public sealed record CreateCommand(
        [property: DataMember] string ChatId,
        [property: DataMember] string UserId,
        [property: DataMember] bool RequireAuthenticated = true
        ) : ICommand<ChatAuthor>, IBackendCommand;

    [DataContract]
    public sealed record ChangeHasLeftCommand(
        [property: DataMember] string AuthorId,
        [property: DataMember] bool HasLeft
    ) : ICommand<ChatAuthor>, IBackendCommand;
}
