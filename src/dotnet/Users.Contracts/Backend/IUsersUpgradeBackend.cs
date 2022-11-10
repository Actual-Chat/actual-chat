namespace ActualChat.Users;

public interface IUsersUpgradeBackend : ICommandService
{
    public Task<ImmutableList<string>> ListAllUserIds(CancellationToken cancellationToken);
}
