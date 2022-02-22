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
    [ComputeMethod(KeepAliveTime = 10)]
    Task<string[]> GetChatIds(Session session, CancellationToken cancellationToken);
    [ComputeMethod(KeepAliveTime = 1)]
    Task<string?> GetChatAuthorAvatarId(Session session, string chatId, CancellationToken cancellationToken);

    // Commands
}
