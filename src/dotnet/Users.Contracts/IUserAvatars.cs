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

    [DataContract]
    public record CreateCommand(
        [property: DataMember] Session Session
        ) : ISessionCommand<UserAvatar>;

    [DataContract]
    public record UpdateCommand(
        [property: DataMember] Session Session,
        [property: DataMember] string AvatarId,
        [property: DataMember] string Name,
        [property: DataMember] string Picture,
        [property: DataMember] string Bio
        ) : ISessionCommand<Unit>;

    [DataContract]
    public record SetDefaultCommand(
        [property: DataMember] Session Session,
        [property: DataMember] string AvatarId
        ) : ISessionCommand<Unit>;
}
