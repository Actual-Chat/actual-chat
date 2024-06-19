using ActualLab.Rpc;
using MemoryPack;

namespace ActualChat.Chat;

public interface IPlacesBackend : IComputeService, IBackendService
{
    [ComputeMethod]
    Task<Place?> Get(PlaceId placeId, CancellationToken cancellationToken);

    // Commands

    [CommandHandler]
    Task<Place> OnChange(PlacesBackend_Change command, CancellationToken cancellationToken);
}

[DataContract, MemoryPackable(GenerateType.VersionTolerant)]
// ReSharper disable once InconsistentNaming
public sealed partial record PlacesBackend_Change(
    [property: DataMember, MemoryPackOrder(0)] PlaceId PlaceId,
    [property: DataMember, MemoryPackOrder(1)] long? ExpectedVersion,
    [property: DataMember, MemoryPackOrder(2)] Change<PlaceDiff> Change,
    [property: DataMember, MemoryPackOrder(3)] UserId OwnerId = default
) : ICommand<Place>, IBackendCommand, IHasShardKey<PlaceId>
{
    [IgnoreDataMember, MemoryPackIgnore]
    public PlaceId ShardKey => PlaceId;
}
