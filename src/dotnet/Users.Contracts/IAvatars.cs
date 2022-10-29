namespace ActualChat.Users;

public interface IAvatars : IComputeService
{
    [ComputeMethod(MinCacheDuration = 10)]
    Task<AvatarFull?> GetOwn(Session session, string avatarId, CancellationToken cancellationToken);
    [ComputeMethod(MinCacheDuration = 10)]
    Task<Avatar?> Get(Session session, string avatarId, CancellationToken cancellationToken);
    [ComputeMethod]
    Task<ImmutableArray<Symbol>> ListOwnAvatarIds(Session session, CancellationToken cancellationToken);

    [CommandHandler]
    Task<AvatarFull> Change(ChangeCommand command, CancellationToken cancellationToken);
    [CommandHandler]
    Task SetDefault(SetDefaultCommand command, CancellationToken cancellationToken);

    [DataContract]
    public sealed record ChangeCommand(
        [property: DataMember] Session Session,
        [property: DataMember] Symbol AvatarId,
        [property: DataMember] long? ExpectedVersion,
        [property: DataMember] Change<AvatarFull> Change
        ) : ISessionCommand<AvatarFull>;

    [DataContract]
    public sealed record SetDefaultCommand(
        [property: DataMember] Session Session,
        [property: DataMember] Symbol AvatarId
        ) : ISessionCommand<Unit>;
}
