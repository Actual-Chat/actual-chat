namespace ActualChat.Users;

public interface IUserProfiles
{
    [ComputeMethod(KeepAliveTime = 10)]
    Task<UserProfile?> Get(Session session, CancellationToken cancellationToken);

    [CommandHandler]
    public Task Update(UpdateCommand command, CancellationToken cancellationToken);

    [DataContract]
    public sealed record UpdateCommand(
        [property: DataMember] Session Session,
        [property: DataMember] UserProfile UserProfile
        ) : ISessionCommand<Unit>;
}
