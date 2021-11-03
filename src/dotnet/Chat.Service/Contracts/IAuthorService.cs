namespace ActualChat.Chat;

/// <summary>
/// Returns the result author (with overrided values). <br/>
/// Methods shouldn't contain <see cref="Session"/> here.
/// </summary>
internal interface IAuthorService
{
    /// <inheritdoc cref="IAuthorServiceFrontend.GetByUserId(Session, UserId, ChatId, CancellationToken)"/>
    [ComputeMethod(KeepAliveTime = 10)]
    Task<Author?> GetByUserIdAndChatId(UserId userId, ChatId chatId, CancellationToken cancellationToken);

    /// <inheritdoc cref="IAuthorServiceFrontend.GetByAuthorId(Session, AuthorId, CancellationToken)"/>
    [ComputeMethod(KeepAliveTime = 10)]
    Task<Author?> GetByAuthorId(AuthorId authorId, CancellationToken cancellationToken);

    [CommandHandler]
    Task<AuthorId> CreateAuthor(CreateAuthorCommand command, CancellationToken cancellationToken);

    public record class CreateAuthorCommand(UserId UserId, ChatId ChatId) : ICommand<AuthorId>;
}