namespace ActualChat.Chat;

/// <summary>
/// Service for managing places and their members.
/// </summary>
public interface IPlaces : IComputeService
{
    [ComputeMethod(MinCacheDuration = 60)]
    Task<Place?> Get(Session session, PlaceId placeId, CancellationToken cancellationToken);
    [ComputeMethod(MinCacheDuration = 60)]
    Task<PlaceRules> GetRules(Session session, PlaceId placeId, CancellationToken cancellationToken);
    [ComputeMethod(MinCacheDuration = 60)]
    Task<PlaceChatId?> GetWelcomeChatId(Session session, PlaceId placeId, CancellationToken cancellationToken);

    [ComputeMethod]
    Task<UserId[]> ListUserIds(Session session, PlaceId placeId, CancellationToken cancellationToken);
    [ComputeMethod]
    Task<AuthorId[]> ListAuthorIds(Session session, PlaceId placeId, CancellationToken cancellationToken);
    [ComputeMethod]
    Task<AuthorId[]> ListOwnerIds(Session session, PlaceId placeId, CancellationToken cancellationToken);
    [ComputeMethod]
    Task<AuthorId[]> ListModeratorIds(Session session, PlaceId placeId, CancellationToken cancellationToken);

    [ComputeMethod]
    Task<AuthorFull?> GetOwn(Session session, PlaceId placeId, CancellationToken cancellationToken);
    [ComputeMethod]
    Task<Author?> Get(Session session, PlaceId placeId, AuthorId authorId, CancellationToken cancellationToken);

    // Commands

    [CommandHandler]
    Task<Place> OnChange(Places_Change command, CancellationToken cancellationToken);
    [CommandHandler]
    Task OnJoin(Places_Join command, CancellationToken cancellationToken);
    [CommandHandler]
    Task OnInvite(Places_Invite command, CancellationToken cancellationToken);
    [CommandHandler]
    Task OnExclude(Places_Exclude command, CancellationToken cancellationToken);
    [CommandHandler]
    Task OnRestore(Places_Restore command, CancellationToken cancellationToken);
    [CommandHandler]
    Task OnChangeRole(Places_ChangeRole command, CancellationToken cancellationToken);
    [Obsolete("2026.08: Use Places_ChangeRole. Old clients only.")]
    [CommandHandler]
    Task OnPromoteToOwner(Places_PromoteToOwner command, CancellationToken cancellationToken);
    [CommandHandler]
    Task OnLeave(Places_Leave command, CancellationToken cancellationToken);
}

[DataContract, MessagePackObject]
// ReSharper disable once InconsistentNaming
public sealed partial record Places_Change : ApiCommand<Place>
{
    [DataMember(Order = 2), Key(2)] public required PlaceId? PlaceId { get; init; }
    [DataMember(Order = 3), Key(3)] public required long? ExpectedVersion { get; init; }
    [DataMember(Order = 4), Key(4)] public required Change<PlaceDiff> Change { get; init; }
}

[DataContract, MessagePackObject]
// ReSharper disable once InconsistentNaming
public sealed partial record Places_Join : ApiCommand<Unit>
{
    [DataMember(Order = 2), Key(2)] public required PlaceId PlaceId { get; init; }
    [DataMember(Order = 3), Key(3)] public Symbol AvatarId { get; init; }
}

[DataContract, MessagePackObject]
// ReSharper disable once InconsistentNaming
public sealed partial record Places_Invite : ApiCommand<Unit>
{
    [DataMember(Order = 2), Key(2)] public required PlaceId PlaceId { get; init; }
    [DataMember(Order = 3), Key(3)] public required UserId[] UserIds { get; init; }
}

[DataContract, MessagePackObject]
// ReSharper disable once InconsistentNaming
public sealed partial record Places_Exclude : ApiCommand<Unit>
{
    [DataMember(Order = 2), Key(2)] public required AuthorId AuthorId { get; init; }
}

[DataContract, MessagePackObject]
// ReSharper disable once InconsistentNaming
public sealed partial record Places_Restore : ApiCommand<Unit>
{
    [DataMember(Order = 2), Key(2)] public required AuthorId AuthorId { get; init; }
}

[DataContract, MessagePackObject]
// ReSharper disable once InconsistentNaming
public sealed partial record Places_ChangeRole : ApiCommand<Unit>
{
    [DataMember(Order = 2), Key(2)] public required AuthorId AuthorId { get; init; }
    [DataMember(Order = 3), Key(3)] public required SystemRole SystemRole { get; init; }
    [DataMember(Order = 4), Key(4)] public required bool IsInRole { get; init; }
}

[DataContract, MessagePackObject]
[Obsolete("2026.08: Use Places_ChangeRole. Old clients only.")]
// ReSharper disable once InconsistentNaming
public sealed partial record Places_PromoteToOwner : ApiCommand<Unit>
{
    [DataMember(Order = 2), Key(2)] public required AuthorId AuthorId { get; init; }
}

[DataContract, MessagePackObject]
// ReSharper disable once InconsistentNaming
public sealed partial record Places_Leave : ApiCommand<Unit>
{
    [DataMember(Order = 2), Key(2)] public required PlaceId PlaceId { get; init; }
}
