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
public sealed partial record Avatars_Change : ApiCommand<AvatarFull>
{
    [DataMember(Order = 2), Key(2)] public required Symbol AvatarId { get; init; }
    [DataMember(Order = 3), Key(3)] public required long? ExpectedVersion { get; init; }
    [DataMember(Order = 4), Key(4)] public required Change<AvatarDiff> Change { get; init; }
}

[DataContract, MessagePackObject]
// ReSharper disable once InconsistentNaming
public sealed partial record Avatars_SetDefault : ApiCommand<Unit>
{
    [DataMember(Order = 2), Key(2)] public required Symbol AvatarId { get; init; }
}
