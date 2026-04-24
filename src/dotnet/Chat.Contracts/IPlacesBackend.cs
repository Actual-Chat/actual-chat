using ActualLab.Rpc;

namespace ActualChat.Chat;

/// <summary>
/// Backend service for managing places (organizational containers for chats).
/// </summary>
public interface IPlacesBackend : IComputeService, IBackendService
{
    [ComputeMethod]
    Task<Place?> Get(PlaceId placeId, CancellationToken cancellationToken);

    // Non-compute methods

    Task<Place[]> ListChanged(
        long minVersion,
        long maxVersion,
        PlaceId? lastId,
        int limit,
        CancellationToken cancellationToken);

    // Commands

    [CommandHandler]
    Task<Place> OnChange(PlacesBackend_Change command, CancellationToken cancellationToken);
}

/// <summary>
/// Command to create, update, or delete a place.
/// </summary>
[DataContract, MemoryPackable(GenerateType.VersionTolerant), MessagePackObject]
// ReSharper disable once InconsistentNaming
public sealed partial record PlacesBackend_Change(
    [property: DataMember, MemoryPackOrder(0), Key(0)] PlaceId? PlaceId,
    [property: DataMember, MemoryPackOrder(1), Key(1)] long? ExpectedVersion,
    [property: DataMember, MemoryPackOrder(2), Key(2)] Change<PlaceDiff> Change,
    [property: DataMember, MemoryPackOrder(3), Key(3)] UserId? OwnerId = null
) : ICommand<Place>, IBackendCommand, IHasShardKey<PlaceId?>
{
    [IgnoreDataMember, MemoryPackIgnore, IgnoreMember]
    public PlaceId? ShardKey => PlaceId;
}
