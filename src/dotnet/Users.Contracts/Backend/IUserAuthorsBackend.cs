namespace ActualChat.Users;

public interface IUserAuthorsBackend
{
    [ComputeMethod(KeepAliveTime = 10)]
    Task<UserAuthor?> Get(UserId userId, bool inherit, CancellationToken cancellationToken);
}
