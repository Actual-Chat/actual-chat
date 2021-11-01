using System.Security;

namespace ActualChat.Chat;

/// <summary>
/// The <see cref="IAuthorServiceFacade"/> implementation which checks permissions,
/// maps to a controller's dto objects and calls the internal <see cref="IAuthorService"/> service. <br/>
/// The service is divided to two parts to exclude a session object from the bigger computed part.
/// </summary>
public class AuthorServiceFacade : IAuthorServiceFacade
{
    private readonly IAuthService _auth;
    private readonly IAuthorService _authorService;

    public AuthorServiceFacade(IAuthService auth, IAuthorService authorService)
    {
        _auth = auth;
        _authorService = authorService;
    }

    /// <inheritdoc />
    public virtual async Task<Author?> GetByUserIdAndChatId(
        Session session,
        UserId userId,
        ChatId chatId,
        CancellationToken cancellationToken)
    {
        await AssertHasPermissions(session, userId, cancellationToken).ConfigureAwait(false);
        return await _authorService.GetByUserIdAndChatId(userId, chatId, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public virtual async Task<AuthorInfo?> GetByAuthorId(Session session, AuthorId authorId, CancellationToken cancellationToken)
    {
        // today we don't have "invisible" status and anyone can read the real author status (but not userId)
        var author = await _authorService.GetByAuthorId(authorId, cancellationToken).ConfigureAwait(false);
        if(author == null)
            return null;
        return new(author);
    }

    [ComputeMethod]
    protected virtual async Task<Unit> AssertHasPermissions(
        Session session,
        UserId userId,
        CancellationToken cancellationToken)
    {
        if (userId.IsNone)
            throw new SecurityException("Not enough permissions.");

        var user = await _auth.GetUser(session, cancellationToken).ConfigureAwait(false);

        if (!user.IsAuthenticated)
            throw new SecurityException("Not enough permissions.");

        // ToDo: ask an external service (via fusion) about the user permissions
        if (userId.Value != user.Id && user.IsInRole("Admin"))
            throw new SecurityException("Not enough permissions.");

        return default;
    }
}

