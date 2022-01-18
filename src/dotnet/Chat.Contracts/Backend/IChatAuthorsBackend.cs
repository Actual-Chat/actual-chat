namespace ActualChat.Chat;

public interface IChatAuthorsBackend
{
    [ComputeMethod(KeepAliveTime = 10)]
    Task<ChatAuthor?> Get(string chatId, string authorId, bool inherit, CancellationToken cancellationToken);
    [ComputeMethod(KeepAliveTime = 10)]
    Task<ChatAuthor?> GetByUserId(string chatId, string userId, bool inherit, CancellationToken cancellationToken);
    [ComputeMethod(KeepAliveTime = 10)]
    Task<string[]> GetChatIdsByUserId(string userId, CancellationToken cancellationToken);
    Task<ChatAuthor> GetOrCreate(Session session, string chatId, CancellationToken cancellationToken);

    // Commands

    [CommandHandler]
    Task<ChatAuthor> Create(CreateCommand command, CancellationToken cancellationToken);
    [CommandHandler]
    Task Update(UpdateCommand command, CancellationToken cancellationToken);

    public record CreateCommand(string ChatId, string UserId) : ICommand<ChatAuthor>, IBackendCommand { }
    public record UpdateCommand(string AuthorId, string Name, string Picture) : ICommand<Unit>, IBackendCommand { }
}
