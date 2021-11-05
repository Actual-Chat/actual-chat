namespace ActualChat.Users;

public interface IUserInfos
{
    [ComputeMethod(KeepAliveTime = 10)]
    Task<UserInfo?> Get(UserId userId, CancellationToken cancellationToken);
    [ComputeMethod(KeepAliveTime = 10)]
    Task<UserInfo?> GetByName(string name, CancellationToken cancellationToken);
}
