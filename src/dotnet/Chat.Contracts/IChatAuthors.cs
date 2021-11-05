using ActualChat.Users;

namespace ActualChat.Chat;

public interface IChatAuthors
{
    [ComputeMethod(KeepAliveTime = 10)]
    Task<ChatAuthor?> GetSessionChatAuthor(Session session, ChatId chatId, CancellationToken cancellationToken);
    [ComputeMethod(KeepAliveTime = 10)]
    Task<Author?> GetAuthor(ChatId chatId, AuthorId authorId, bool inherit, CancellationToken cancellationToken);
}
