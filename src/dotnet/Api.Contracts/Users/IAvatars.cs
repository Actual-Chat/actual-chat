namespace ActualChat.Users;

/// <summary>
/// Service for managing user avatars.
/// </summary>
public interface IAvatars : IComputeService
{
    [ComputeMethod(MinCacheDuration = 10)]
    Task<AvatarFull?> GetOwn(Session session, Symbol avatarId, CancellationToken cancellationToken);
    [ComputeMethod(MinCacheDuration = 10)]
    Task<Avatar?> Get(Session session, Symbol avatarId, CancellationToken cancellationToken);
    [ComputeMethod]
    Task<IReadOnlyList<Symbol>> ListOwnAvatarIds(Session session, CancellationToken cancellationToken);

    [CommandHandler]
    Task<AvatarFull> OnChange(Avatars_Change command, CancellationToken cancellationToken);
    [CommandHandler]
    Task OnSetDefault(Avatars_SetDefault command, CancellationToken cancellationToken);
}

[DataContract, MessagePackObject]
// ReSharper disable once InconsistentNaming
public sealed partial record Avatars_Change(
    [property: DataMember, Key(0)] Session Session,
    [property: DataMember, Key(1)] Symbol AvatarId,
    [property: DataMember, Key(2)] long? ExpectedVersion,
    [property: DataMember, Key(3)] Change<AvatarDiff> Change
) : ISessionCommand<AvatarFull>, IApiCommand;

[DataContract, MessagePackObject]
// ReSharper disable once InconsistentNaming
public sealed partial record Avatars_SetDefault(
    [property: DataMember, Key(0)] Session Session,
    [property: DataMember, Key(1)] Symbol AvatarId
) : ISessionCommand<Unit>, IApiCommand;
