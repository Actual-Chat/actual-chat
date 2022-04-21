namespace ActualChat.Users;

public interface IAuthz
{
    [ComputeMethod(KeepAliveTime = 10)]
    public Task<bool> IsActive(Session session, CancellationToken cancellationToken);
}
