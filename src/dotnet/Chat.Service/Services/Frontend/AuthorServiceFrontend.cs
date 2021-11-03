namespace ActualChat.Chat;

/// <summary>
/// The <see cref="IAuthorServiceFrontend"/> implementation which checks permissions,
/// maps to a controller's dto objects and calls the internal <see cref="IAuthorService"/> service. <br/>
/// The service is divided to two parts to exclude a session object from the bigger computed part.
/// </summary>
internal class AuthorServiceFrontend : IAuthorServiceFrontend
{
    private readonly IAuthorService _authorService;

    public AuthorServiceFrontend(IAuthorService authorService) => _authorService = authorService;

    /// <inheritdoc />
    public virtual async Task<AuthorInfo?> GetByAuthorId(Session session, AuthorId authorId, CancellationToken cancellationToken)
    {
        // today we don't have "invisible" status and anyone can read the real author status (but not userId)
        var author = await _authorService.GetByAuthorId(authorId, cancellationToken).ConfigureAwait(false);
        if (author == null)
            return null;
        return new(author);
    }
}

