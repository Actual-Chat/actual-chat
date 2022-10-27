namespace ActualChat.Users;

public interface IAvatars : IComputeService
{
    [ComputeMethod(MinCacheDuration = 10)]
    Task<Avatar?> Get(string avatarId, CancellationToken cancellationToken);
    [ComputeMethod(MinCacheDuration = 10)]
    Task<AvatarFull?> GetOwn(Session session, string avatarId, CancellationToken cancellationToken);
    [ComputeMethod]
    Task<ImmutableArray<Symbol>> ListAvatarIds(Session session, CancellationToken cancellationToken);

    [CommandHandler]
    Task<AvatarFull> Change(ChangeCommand command, CancellationToken cancellationToken);
    [CommandHandler]
    Task SetDefault(SetDefaultCommand command, CancellationToken cancellationToken);

    [DataContract]
    public sealed record ChangeCommand(
        [property: DataMember] Session Session,
        [property: DataMember] string AvatarId,
        [property: DataMember] Change<AvatarFull> Change
        ) : ISessionCommand<AvatarFull>;

    [DataContract]
    public sealed record SetDefaultCommand(
        [property: DataMember] Session Session,
        [property: DataMember] string AvatarId
        ) : ISessionCommand<Unit>;
}
