namespace ActualChat.Users;

public interface IUserPresences : IComputeService
{
    [ComputeMethod(MinCacheDuration = 30)]
    Task<Presence> Get(string userId, CancellationToken cancellationToken);
}
