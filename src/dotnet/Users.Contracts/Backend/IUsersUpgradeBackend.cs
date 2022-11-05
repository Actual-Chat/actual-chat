namespace ActualChat.Users;

public interface IUsersUpgradeBackend
{
    public Task<ImmutableList<string>> ListAllUserIds(CancellationToken cancellationToken);
}
