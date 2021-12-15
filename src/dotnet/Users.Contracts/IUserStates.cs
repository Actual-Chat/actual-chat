namespace ActualChat.Users;

public interface IUserStates
{
    [ComputeMethod(KeepAliveTime = 30)]
    Task<bool> IsOnline(string userId, CancellationToken cancellationToken);
}
