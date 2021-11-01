namespace ActualChat.Chat;

/// <summary>
/// Reads <see cref="AuthorId"/> from the <see cref="Session"/>
/// </summary>
public interface IAuthorIdAccessor
{
    /// <inheritdoc cref="IAuthorIdAccessor"/>
    ValueTask<AuthorId> Get(Session session);
}
