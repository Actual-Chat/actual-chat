namespace ActualChat.Users;

public interface IUsersTempBackend : IComputeService
{
    public Task<ImmutableList<string>> ListUserIds(CancellationToken cancellationToken);
}
