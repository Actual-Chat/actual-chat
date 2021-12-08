namespace ActualChat.Users;

public interface IUserAuthorsBackend
{
    [ComputeMethod(KeepAliveTime = 10)]
    Task<UserAuthor?> Get(string userId, bool inherit, CancellationToken cancellationToken);
}
