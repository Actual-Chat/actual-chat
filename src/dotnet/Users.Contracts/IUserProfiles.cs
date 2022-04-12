namespace ActualChat.Users;

public interface IUserProfiles
{
    [ComputeMethod(KeepAliveTime = 10)]
    Task<UserProfile?> Get(Session session, CancellationToken cancellationToken);

    [CommandHandler]
    public Task UpdateStatus(UpdateStatusCommand command, CancellationToken cancellationToken);

    public record UpdateStatusCommand(string UserProfileId, UserStatus NewStatus, Session Session) : ISessionCommand<Unit>;
}
