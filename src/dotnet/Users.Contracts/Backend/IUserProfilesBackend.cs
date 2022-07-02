namespace ActualChat.Users;

public interface IUserProfilesBackend : IComputeService
{
    [ComputeMethod(KeepAliveTime = 10)]
    Task<UserProfile?> Get(string id, CancellationToken cancellationToken);
    [ComputeMethod(KeepAliveTime = 10)]
    Task<UserAuthor?> GetUserAuthor(string userId, CancellationToken cancellationToken);

    [CommandHandler]
    public Task Update(UpdateCommand command, CancellationToken cancellationToken);

    [DataContract]
    public sealed record UpdateCommand(
        [property: DataMember] UserProfile UserProfile
        ) : ICommand<Unit>, IBackendCommand;
}
