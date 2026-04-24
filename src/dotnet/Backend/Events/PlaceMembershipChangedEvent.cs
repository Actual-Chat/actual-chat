using ActualChat.Contacts;

namespace ActualChat;

[DataContract, MemoryPackable(GenerateType.VersionTolerant), MessagePackObject(true)]
public sealed partial record PlaceMembershipChangedEvent(
    [property: DataMember, MemoryPackOrder(1)] UserId UserId,
    [property: DataMember, MemoryPackOrder(2)] PlaceId PlaceId,
    [property: DataMember, MemoryPackOrder(3)] bool HasLeft) : EventCommand, IHasShardKey<UserId>
{
    [IgnoreDataMember, MemoryPackIgnore, IgnoreMember]
    public UserId ShardKey => UserId;
}
