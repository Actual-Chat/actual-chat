namespace ActualChat.Chat;

/// <summary> Reads <see cref="AuthorId"/> from the <see cref="Session"/> </summary>
public interface IAuthorIdAccessor
{
    /// <inheritdoc cref="IAuthorIdAccessor"/>
    Task<AuthorId> Get(Session session, ChatId chatId, CancellationToken cancellationToken);
}
