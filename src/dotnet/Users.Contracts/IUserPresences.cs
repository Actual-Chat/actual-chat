namespace ActualChat.Users;

public interface IUserPresences
{
    [ComputeMethod(KeepAliveTime = 30)]
    Task<Presence> Get(string userId, CancellationToken cancellationToken);
}
