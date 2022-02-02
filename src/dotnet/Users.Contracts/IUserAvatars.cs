namespace ActualChat.Users;

public interface IUserAvatars
{
    [ComputeMethod(KeepAliveTime = 10)]
    Task<UserAvatar?> Get(Session session, string avatarId, CancellationToken cancellationToken);

    [ComputeMethod(KeepAliveTime = 10)]
    Task<string[]> GetAvatarIds(Session session, CancellationToken cancellationToken);

    [ComputeMethod(KeepAliveTime = 10)]
    Task<string> GetDefaultAvatarId(Session session, CancellationToken cancellationToken);

    [CommandHandler]
    Task<UserAvatar> Create(CreateCommand command, CancellationToken cancellationToken);
    [CommandHandler]
    Task Update(UpdateCommand command, CancellationToken cancellationToken);
    [CommandHandler]
    Task SetDefault(SetDefaultCommand command, CancellationToken cancellationToken);

    public record CreateCommand(Session Session) : ISessionCommand<UserAvatar>;
    public record UpdateCommand(Session Session, string AvatarId, string Name, string Picture, string Bio) : ISessionCommand<Unit>;
    public record SetDefaultCommand(Session Session, string AvatarId) : ISessionCommand<Unit>;
}
