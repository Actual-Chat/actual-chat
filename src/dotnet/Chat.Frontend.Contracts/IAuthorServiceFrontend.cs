namespace ActualChat.Chat;

/// <summary>
/// The external part of the <see cref="IAuthorService"/>.
/// </summary>
public interface IAuthorServiceFrontend
{
    /// <summary>
    /// Returns the final <see cref="Author"/> object for <paramref name="authorId"/>. <br />
    /// If <paramref name="authorId"/> isn't presented should return a placeholder author object.
    /// </summary>
    [ComputeMethod(KeepAliveTime = 10)]
    Task<AuthorInfo?> GetByAuthorId(Session session, AuthorId authorId, CancellationToken cancellationToken);
}
