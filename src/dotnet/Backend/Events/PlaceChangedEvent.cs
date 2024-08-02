using ActualChat.Chat;
using MemoryPack;

namespace ActualChat.Backend.Events;

[DataContract, MemoryPackable(GenerateType.VersionTolerant)]
public partial record PlaceChangedEvent(
    [property: DataMember, MemoryPackOrder(1)] Place Place,
    [property: DataMember, MemoryPackOrder(2)] Place? OldPlace,
    [property: DataMember, MemoryPackOrder(3)] ChangeKind ChangeKind
) : EventCommand, IHasShardKey<PlaceId>
{
    [IgnoreDataMember, MemoryPackIgnore]
    public PlaceId ShardKey => Place.Id;
}
