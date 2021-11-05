namespace ActualChat.Chat;

public interface IChatAuthorsBackend
{
    [ComputeMethod(KeepAliveTime = 10)]
    Task<ChatAuthor?> Get(ChatId chatId, AuthorId authorId, bool inherit, CancellationToken cancellationToken);
    [ComputeMethod(KeepAliveTime = 10)]
    Task<ChatAuthor?> GetByUserId(ChatId chatId, UserId userId, bool inherit, CancellationToken cancellationToken);
    Task<ChatAuthor> GetOrCreate(Session session, ChatId chatId, CancellationToken cancellationToken);

    // Commands

    [CommandHandler]
    Task<ChatAuthor> Create(CreateAuthorCommand command, CancellationToken cancellationToken);

    public record CreateAuthorCommand(ChatId ChatId, UserId UserId) : ICommand<ChatAuthor> { }
}
