namespace ActualChat.Users;

public interface IUserPresences : IComputeService
{
    [ComputeMethod(MinCacheDuration = 30)]
    Task<Presence> Get(UserId userId, CancellationToken cancellationToken);
}
