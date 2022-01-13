using ActualChat.Users;

namespace ActualChat.Chat;

public interface IChatAuthors
{
    [ComputeMethod(KeepAliveTime = 10)]
    Task<ChatAuthor?> GetChatAuthor(Session session, string chatId, CancellationToken cancellationToken);
    [ComputeMethod(KeepAliveTime = 10)]
    Task<string> GetChatPrincipalId(Session session, string chatId, CancellationToken cancellationToken);
    [ComputeMethod(KeepAliveTime = 10)]
    Task<Author?> GetAuthor(string chatId, string authorId, bool inherit, CancellationToken cancellationToken);

    // Commands

    [CommandHandler]
    Task UpdateAuthor(UpdateAuthorCommand command, CancellationToken cancellationToken);

    public record UpdateAuthorCommand(Session Session, string ChatId, string Name, string Picture) : ISessionCommand<Unit>;
}
