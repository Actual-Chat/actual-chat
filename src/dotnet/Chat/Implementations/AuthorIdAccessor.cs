namespace ActualChat.Chat;

public class AuthorIdAccessor : IAuthorIdAccessor
{
    private readonly IAuthService _authService;

    public AuthorIdAccessor(IAuthService authService) => _authService = authService;

    public async Task<AuthorId> Get(Session session, ChatId chatId, CancellationToken cancellationToken)
        => new((await GetFromSession(session, chatId, cancellationToken).ConfigureAwait(false))!);

    private async Task<string?> GetFromSession(Session session, ChatId chatId, CancellationToken cancellationToken)
    {
        var sessionInfo = await _authService.GetSessionInfo(session, cancellationToken).ConfigureAwait(false);
        return sessionInfo.Options[$"{chatId}::authorId"] as string;
    }
}
