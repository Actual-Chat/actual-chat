namespace ActualChat.Chat;

public interface IAuthorServiceBackend
{
    /// <summary>
    /// Returns the final <see cref="Author"/> object for (<paramref name="userId"/>, <paramref name="chatId"/>)
    /// </summary>
    // TODO: remove AutoInvalidateTime after adding profile page
    //[ComputeMethod(KeepAliveTime = 10, AutoInvalidateTime = 20)]
    Task<Author?> GetByUserIdAndChatId(UserId userId, ChatId chatId, CancellationToken cancellationToken);

    Task<AuthorId> GetOrCreate(Session session, ChatId chatId, CancellationToken cancellationToken);
}
