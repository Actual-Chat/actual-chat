namespace ActualChat.Chat;

/// <summary>
/// The external part of the <see cref="IAuthorService"/>.
/// </summary>
public interface IAuthorServiceFacade
{
    /// <summary>
    /// Returns the final <see cref="Author"/> object for (<paramref name="userId"/>, <paramref name="chatId"/>)
    /// Throws <seealso cref="System.Security.SecurityException"/> if <paramref name="userId"/>
    /// isn't current user and you don't have an admin rights.
    /// </summary>
    // TODO: remove AutoInvalidateTime after adding profile page
    [ComputeMethod(KeepAliveTime = 10, AutoInvalidateTime = 20)]
    Task<Author?> GetByUserIdAndChatId(Session session, UserId userId, ChatId chatId, CancellationToken cancellationToken);

    /// <summary>
    /// Returns the final <see cref="Author"/> object for <paramref name="authorId"/>. <br />
    /// If <paramref name="authorId"/> isn't presented should return a placeholder author object.
    /// </summary>
    [ComputeMethod(KeepAliveTime = 10)]
    Task<AuthorInfo?> GetByAuthorId(Session session, AuthorId authorId, CancellationToken cancellationToken);
}

/// <summary>
/// Returns the result author (with overrided values). <br/>
/// Clients should use <seealso cref="IAuthorServiceFacade"/> <br/>
/// Methods shouldn't contain <see cref="Session"/> here.
/// </summary>
public interface IAuthorService
{
    /// <inheritdoc cref="IAuthorServiceFacade.GetByUserId(Session, UserId, ChatId, CancellationToken)"/>
    [ComputeMethod(KeepAliveTime = 10)]
    Task<Author?> GetByUserIdAndChatId(UserId userId, ChatId chatId, CancellationToken cancellationToken);

    /// <inheritdoc cref="IAuthorServiceFacade.GetByAuthorId(Session, AuthorId, CancellationToken)"/>
    [ComputeMethod(KeepAliveTime = 10)]
    Task<Author?> GetByAuthorId(AuthorId authorId, CancellationToken cancellationToken);

    [CommandHandler, Internal]
    Task<AuthorId> CreateAuthor(CreateAuthorCommand command, CancellationToken cancellationToken);

    public record class CreateAuthorCommand(UserId UserId, ChatId ChatId) : ICommand<AuthorId>;
}