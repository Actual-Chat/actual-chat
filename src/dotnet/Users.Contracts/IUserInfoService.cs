namespace ActualChat.Users;

public interface IUserInfoService
{
    [ComputeMethod(KeepAliveTime = 10)]
    Task<UserInfo?> Get(UserId userId, CancellationToken cancellationToken);
    [ComputeMethod(KeepAliveTime = 10)]
    Task<UserInfo?> GetByName(string name, CancellationToken cancellationToken);
}
