namespace ActualChat.Chat;

public interface IChatAuthorsBackend
{
    [ComputeMethod(KeepAliveTime = 10)]
    Task<ChatAuthor?> Get(string chatId, string authorId, bool inherit, CancellationToken cancellationToken);
    [ComputeMethod(KeepAliveTime = 10)]
    Task<ChatAuthor?> GetByUserId(string chatId, string userId, bool inherit, CancellationToken cancellationToken);
    [ComputeMethod(KeepAliveTime = 10)]
    Task<ImmutableArray<Symbol>> ListAuthorIds(string chatId, CancellationToken cancellationToken);
    [ComputeMethod(KeepAliveTime = 10)]
    Task<ImmutableArray<Symbol>> ListUserIds(string chatId, CancellationToken cancellationToken);
    // TODO(AY): Move this method to IUsersBackend
    [ComputeMethod(KeepAliveTime = 10)]
    Task<ImmutableArray<Symbol>> ListUserChatIds(string userId, CancellationToken cancellationToken);

    // Non-compute methods

    Task<ChatAuthor> GetOrCreate(Session session, string chatId, CancellationToken cancellationToken);

    // Commands

    [CommandHandler]
    Task<ChatAuthor> Create(CreateCommand command, CancellationToken cancellationToken);

    [DataContract]
    public sealed record CreateCommand(
        [property: DataMember] string ChatId,
        [property: DataMember] string UserId
        ) : ICommand<ChatAuthor>, IBackendCommand;
}
