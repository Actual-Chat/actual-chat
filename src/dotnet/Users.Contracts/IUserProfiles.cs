namespace ActualChat.Users;

public interface IUserProfiles
{
    [ComputeMethod(KeepAliveTime = 10)]
    Task<UserProfile?> Get(Session session, CancellationToken cancellationToken);
}
