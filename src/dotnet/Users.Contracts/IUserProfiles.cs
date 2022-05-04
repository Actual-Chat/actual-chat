namespace ActualChat.Users;

public interface IUserProfiles
{
    [ComputeMethod(KeepAliveTime = 10)]
    Task<UserProfile?> Get(Session session, CancellationToken cancellationToken);

    [CommandHandler]
    public Task UpdateStatus(UpdateStatusCommand command, CancellationToken cancellationToken);

    [DataContract]
    public record UpdateStatusCommand(
        [property: DataMember] string UserProfileId,
        [property: DataMember] UserStatus NewStatus,
        [property: DataMember] Session Session
        ) : ISessionCommand<Unit>;
}
