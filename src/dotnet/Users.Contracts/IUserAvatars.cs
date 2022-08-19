namespace ActualChat.Users;

public interface IUserAvatars : IComputeService
{
    [ComputeMethod(MinCacheDuration = 10)]
    Task<UserAvatar?> Get(Session session, string avatarId, CancellationToken cancellationToken);
    [ComputeMethod(MinCacheDuration = 10)]
    Task<Symbol> GetDefaultAvatarId(Session session, CancellationToken cancellationToken);
    [ComputeMethod]
    Task<ImmutableArray<Symbol>> ListAvatarIds(Session session, CancellationToken cancellationToken);

    [CommandHandler]
    Task<UserAvatar> Create(CreateCommand command, CancellationToken cancellationToken);
    [CommandHandler]
    Task Update(UpdateCommand command, CancellationToken cancellationToken);
    [CommandHandler]
    Task SetDefault(SetDefaultCommand command, CancellationToken cancellationToken);

    [DataContract]
    public sealed record CreateCommand(
        [property: DataMember] Session Session
        ) : ISessionCommand<UserAvatar>;

    [DataContract]
    public sealed record UpdateCommand(
        [property: DataMember] Session Session,
        [property: DataMember] string AvatarId,
        [property: DataMember] string Name,
        [property: DataMember] string Picture,
        [property: DataMember] string Bio
        ) : ISessionCommand<Unit>;

    [DataContract]
    public sealed record SetDefaultCommand(
        [property: DataMember] Session Session,
        [property: DataMember] string AvatarId
        ) : ISessionCommand<Unit>;
}
