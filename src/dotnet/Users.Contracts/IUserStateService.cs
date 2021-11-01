namespace ActualChat.Users;

public interface IUserStateService
{
    [ComputeMethod(KeepAliveTime = 30)]
    Task<bool> IsOnline(UserId userId, CancellationToken cancellationToken);
}
