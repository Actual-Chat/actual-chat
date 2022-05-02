namespace ActualChat.Users;

public interface IUserProfilesBackend
{
    [ComputeMethod(KeepAliveTime = 10)]
    Task<UserProfile?> Get(string id, CancellationToken cancellationToken);
    [ComputeMethod(KeepAliveTime = 10)]
    Task<UserProfile?> GetByName(string name, CancellationToken cancellationToken);

    [CommandHandler]
    public Task Create(CreateCommand command, CancellationToken cancellationToken);

    [CommandHandler]
    public Task Update(UpdateCommand command, CancellationToken cancellationToken);

    public record CreateCommand(string UserProfileOrUserId) : ICommand<Unit>, IBackendCommand;

    public record UpdateCommand(UserProfile UserProfile) : ICommand<Unit>, IBackendCommand;
}
