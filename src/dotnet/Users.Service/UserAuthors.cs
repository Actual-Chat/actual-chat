namespace ActualChat.Users;

public class UserAuthors : IUserAuthors
{
    private readonly IUserAuthorsBackend _userAuthorsBackend;

    public UserAuthors(IUserAuthorsBackend userAuthorsBackend)
        => _userAuthorsBackend = userAuthorsBackend;

    // [ComputeMethod]
    public virtual Task<UserAuthor?> Get(string userId, bool inherit, CancellationToken cancellationToken)
        => _userAuthorsBackend.Get(userId, inherit, cancellationToken);
}
