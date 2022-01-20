namespace ActualChat.Users;

public interface IUserInfos
{
    [ComputeMethod(KeepAliveTime = 10)]
    Task<UserInfo?> Get(string userId, CancellationToken cancellationToken);

    // TODO(AY): Move to backend; the proper impl. of this method will end up hitting every user cluster
    [ComputeMethod(KeepAliveTime = 10)]
    Task<UserInfo?> GetByName(string name, CancellationToken cancellationToken);

    [ComputeMethod(KeepAliveTime = 10)]
    Task<string> GetGravatarHash(string userId, CancellationToken cancellationToken);
}
