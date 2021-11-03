using ActualChat.Users;

namespace ActualChat.Chat;

internal class AuthorServiceBackend : IAuthorServiceBackend
{
    private readonly IAuthorIdAccessor _authorIdAccessor;
    private readonly IAuthorService _service;
    private readonly ICommander _commander;
    private readonly IAuthService _authService;

    public AuthorServiceBackend(
        IAuthorIdAccessor authorIdAccessor,
        IAuthorService service,
        ICommander commander,
        IAuthService authService)
    {
        _authorIdAccessor = authorIdAccessor;
        _service = service;
        _commander = commander;
        _authService = authService;
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
        var user = await _authService.GetUser(session, cancellationToken).ConfigureAwait(false);
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
            new ISessionInfoService.UpsertCommand(session, new($"{chatId}::authorId", authorId)),
            isolate: true,
            cancellationToken
        ).ConfigureAwait(false);
        return authorId.ToString();
    }
}