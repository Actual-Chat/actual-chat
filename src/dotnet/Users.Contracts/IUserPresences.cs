namespace ActualChat.Users;

public interface IUserPresences : IComputeService
{
    [ComputeMethod(MinCacheDuration = 30)]
    Task<Presence> Get(UserId userId, CancellationToken cancellationToken);

    [CommandHandler]
    public Task CheckIn(CheckInCommand command, CancellationToken cancellationToken);

    [DataContract]
    public sealed record CheckInCommand(
        [property: DataMember] Session Session,
        [property: DataMember] bool IsActive
    ) : ISessionCommand<Unit>;
}
