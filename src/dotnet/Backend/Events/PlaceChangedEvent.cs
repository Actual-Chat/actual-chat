using ActualChat.Chat;

namespace ActualChat;

[DataContract, MemoryPackable(GenerateType.VersionTolerant), MessagePackObject(true)]
public partial record PlaceChangedEvent(
    [property: DataMember, MemoryPackOrder(1)] Place Place,
    [property: DataMember, MemoryPackOrder(2)] Place? OldPlace,
    [property: DataMember, MemoryPackOrder(3)] ChangeKind ChangeKind
) : EventCommand, IHasShardKey<PlaceId>
{
    [IgnoreDataMember, MemoryPackIgnore, IgnoreMember]
    public PlaceId ShardKey => Place.Id;
}
