using ActualChat.Users;

namespace ActualChat.Chat;

internal class AuthorServiceBackend : IAuthorServiceBackend
{
    private readonly IAuthorIdAccessor _authorIdAccessor;
    private readonly IAuthorService _service;
    private readonly IAuth _auth;
    private readonly ICommander _commander;

    public AuthorServiceBackend(
        IAuthorIdAccessor authorIdAccessor,
        IAuthorService service,
        ICommander commander,
        IAuth auth)
    {
        _authorIdAccessor = authorIdAccessor;
        _service = service;
        _auth = auth;
        _commander = commander;
    }

    /// <inheritdoc />
    public virtual Task<Author?> GetByUserIdAndChatId(UserId userId, ChatId chatId, CancellationToken cancellationToken)
        => _service.GetByUserIdAndChatId(userId, chatId, cancellationToken);


    public async virtual Task<AuthorId> GetOrCreate(Session session, ChatId chatId, CancellationToken cancellationToken)
    {
        var authorId = await _authorIdAccessor.Get(session, chatId, cancellationToken).ConfigureAwait(false);
        if (authorId.IsNone) {
            authorId = await GetOrCreateFromDatabase(session, chatId, cancellationToken).ConfigureAwait(false);
        }
        return authorId;
    }

    private async Task<string> GetOrCreateFromDatabase(
        Session session,
        ChatId chatId,
        CancellationToken cancellationToken)
    {
        var user = await _auth.GetSessionUser(session, cancellationToken).ConfigureAwait(false);
        var author = await _service.GetByUserIdAndChatId(user.Id, chatId, cancellationToken).ConfigureAwait(false);
        var authorId = author?.AuthorId ?? AuthorId.None;
        // the author service doesn't expect the userId like @guest/{Ulid}
        var userId = user.IsAuthenticated ? user.Id : UserId.None;
        if (authorId.IsNone) {
            authorId = await _commander.Call(
                new IAuthorService.CreateAuthorCommand(userId, chatId),
                isolate: true,
                cancellationToken
            ).ConfigureAwait(false);
        }
        await _commander.Call(
            new ISessionOptionsBackend.UpdateCommand(
                session,
                new($"{chatId}::authorId", authorId)
                ).MarkValid(),
            isolate: true,
            cancellationToken
        ).ConfigureAwait(false);
        return authorId.ToString();
    }
}
