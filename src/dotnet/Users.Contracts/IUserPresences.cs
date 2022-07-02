namespace ActualChat.Users;

public interface IUserPresences : IComputeService
{
    [ComputeMethod(KeepAliveTime = 30)]
    Task<Presence> Get(string userId, CancellationToken cancellationToken);
}
