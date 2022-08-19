namespace ActualChat.Users;

public interface IUsersTempBackend : IComputeService
{
    public Task<ImmutableArray<string>> GetUserIds(CancellationToken cancellationToken);
}
