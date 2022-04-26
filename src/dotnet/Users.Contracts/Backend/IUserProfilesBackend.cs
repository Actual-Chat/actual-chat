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
    public Task UpdateStatus(UpdateStatusCommand command, CancellationToken cancellationToken);

    public record CreateCommand(string UserProfileOrUserId) : ICommand<Unit>, IBackendCommand;

    public record UpdateStatusCommand(string UserProfileId, UserStatus NewStatus) : ICommand<Unit>, IBackendCommand;
}
