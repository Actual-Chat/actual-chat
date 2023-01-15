namespace ActualChat.Users;

public interface IUsersUpgradeBackend : ICommandService
{
    public Task<ImmutableList<UserId>> ListAllUserIds(CancellationToken cancellationToken);
}
