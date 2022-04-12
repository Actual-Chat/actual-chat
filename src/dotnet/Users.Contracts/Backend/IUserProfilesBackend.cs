namespace ActualChat.Users;

public interface IUserProfilesBackend
{
    [ComputeMethod(KeepAliveTime = 10)]
    Task<UserProfile?> Get(string userId, CancellationToken cancellationToken);
    [ComputeMethod(KeepAliveTime = 10)]
    Task<UserProfile?> GetByName(string name, CancellationToken cancellationToken);
}
