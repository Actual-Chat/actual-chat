using ActualChat.Contacts;
using MemoryPack;

namespace ActualChat;

[DataContract, MemoryPackable(GenerateType.VersionTolerant)]
public sealed partial record PlaceMembershipChangedEvent(
    [property: DataMember, MemoryPackOrder(1)] UserId UserId,
    [property: DataMember, MemoryPackOrder(2)] PlaceId PlaceId,
    [property: DataMember, MemoryPackOrder(3)] bool HasLeft) : EventCommand, IHasShardKey<UserId>
{
    [IgnoreDataMember, MemoryPackIgnore]
    public UserId ShardKey => UserId;
}
