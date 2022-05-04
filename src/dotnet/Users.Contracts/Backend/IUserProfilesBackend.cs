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

    [DataContract]
    public record CreateCommand(
        [property: DataMember] string UserProfileOrUserId
        ) : ICommand<Unit>, IBackendCommand;

    [DataContract]
    public record UpdateStatusCommand(
        [property: DataMember] string UserProfileId,
        [property: DataMember] UserStatus NewStatus
        ) : ICommand<Unit>, IBackendCommand;
}
