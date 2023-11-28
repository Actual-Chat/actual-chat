using MemoryPack;

namespace ActualChat.Chat;

public interface IPlaces : IComputeService
{
    [ComputeMethod(MinCacheDuration = 60), ClientComputeMethod(ClientCacheMode = ClientCacheMode.Cache)]
    Task<Place?> Get(Session session, PlaceId placeId, CancellationToken cancellationToken);

    // Commands

    [CommandHandler]
    Task<Place> OnChange(Places_Change command, CancellationToken cancellationToken);

    [CommandHandler]
    Task OnJoin(Places_Join command, CancellationToken cancellationToken);

    [CommandHandler]
    Task OnInvite(Places_Invite command, CancellationToken cancellationToken);
}

[DataContract, MemoryPackable(GenerateType.VersionTolerant)]
// ReSharper disable once InconsistentNaming
public sealed partial record Places_Change(
    [property: DataMember, MemoryPackOrder(0)] Session Session,
    [property: DataMember, MemoryPackOrder(1)] PlaceId PlaceId,
    [property: DataMember, MemoryPackOrder(2)] long? ExpectedVersion,
    [property: DataMember, MemoryPackOrder(3)] Change<PlaceDiff> Change
) : ISessionCommand<Place>;

[DataContract, MemoryPackable(GenerateType.VersionTolerant)]
// ReSharper disable once InconsistentNaming
public sealed partial record Places_Join(
    [property: DataMember, MemoryPackOrder(0)] Session Session,
    [property: DataMember, MemoryPackOrder(1)] PlaceId PlaceId,
    [property: DataMember, MemoryPackOrder(2)] Symbol AvatarId = default
) : ISessionCommand<Unit>;

[DataContract, MemoryPackable(GenerateType.VersionTolerant)]
// ReSharper disable once InconsistentNaming
public sealed partial record Places_Invite(
    [property: DataMember, MemoryPackOrder(0)] Session Session,
    [property: DataMember, MemoryPackOrder(1)] PlaceId PlaceId,
    [property: DataMember, MemoryPackOrder(2)] UserId[] UserIds
) : ISessionCommand<Unit>;
