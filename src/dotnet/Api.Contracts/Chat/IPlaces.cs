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
    Task OnPromoteToOwner(Places_PromoteToOwner command, CancellationToken cancellationToken);
    [CommandHandler]
    Task OnLeave(Places_Leave command, CancellationToken cancellationToken);
}

[DataContract, MessagePackObject]
// ReSharper disable once InconsistentNaming
public sealed partial record Places_Change(
    [property: DataMember, Key(0)] Session Session,
    [property: DataMember, Key(1)] PlaceId? PlaceId,
    [property: DataMember, Key(2)] long? ExpectedVersion,
    [property: DataMember, Key(3)] Change<PlaceDiff> Change
) : ISessionCommand<Place>, IApiCommand;

[DataContract, MessagePackObject]
// ReSharper disable once InconsistentNaming
public sealed partial record Places_Join(
    [property: DataMember, Key(0)] Session Session,
    [property: DataMember, Key(1)] PlaceId PlaceId,
    [property: DataMember, Key(2)] Symbol AvatarId = default
) : ISessionCommand<Unit>, IApiCommand;

[DataContract, MessagePackObject]
// ReSharper disable once InconsistentNaming
public sealed partial record Places_Invite(
    [property: DataMember, Key(0)] Session Session,
    [property: DataMember, Key(1)] PlaceId PlaceId,
    [property: DataMember, Key(2)] UserId[] UserIds
) : ISessionCommand<Unit>, IApiCommand;

[DataContract, MessagePackObject]
// ReSharper disable once InconsistentNaming
public sealed partial record Places_Exclude(
    [property: DataMember, Key(0)] Session Session,
    [property: DataMember, Key(1)] AuthorId AuthorId
) : ISessionCommand<Unit>, IApiCommand;

[DataContract, MessagePackObject]
// ReSharper disable once InconsistentNaming
public sealed partial record Places_Restore(
    [property: DataMember, Key(0)] Session Session,
    [property: DataMember, Key(1)] AuthorId AuthorId
) : ISessionCommand<Unit>, IApiCommand;

[DataContract, MessagePackObject]
// ReSharper disable once InconsistentNaming
public sealed partial record Places_PromoteToOwner(
    [property: DataMember, Key(0)] Session Session,
    [property: DataMember, Key(1)] AuthorId AuthorId
) : ISessionCommand<Unit>, IApiCommand;

[DataContract, MessagePackObject]
// ReSharper disable once InconsistentNaming
public sealed partial record Places_Leave(
    [property: DataMember, Key(0)] Session Session,
    [property: DataMember, Key(1)] PlaceId PlaceId
) : ISessionCommand<Unit>, IApiCommand;
