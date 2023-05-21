namespace ActualChat.Users;

public interface IUserPresencesBackend : IComputeService
{
    [ComputeMethod(MinCacheDuration = 30)]
    Task<Presence> Get(UserId userId, CancellationToken cancellationToken);

    [CommandHandler]
    public Task CheckIn(CheckInCommand command, CancellationToken cancellationToken);

    [DataContract]
    public sealed record CheckInCommand(
        [property: DataMember] UserId UserId,
        [property: DataMember] Moment At,
        [property: DataMember] bool IsActive
    ) : ICommand<Unit>, IBackendCommand;
}
