namespace ActualChat;

[DataContract, MemoryPackable(GenerateType.VersionTolerant), MessagePackObject(true)]
public partial record UserSignedInEvent(
    [property: DataMember, MemoryPackOrder(1)] Symbol SessionId,
    [property: DataMember, MemoryPackOrder(2)] UserId UserId
) : EventCommand, IHasShardKey<UserId>
{
    [IgnoreDataMember, MemoryPackIgnore, IgnoreMember]
    public UserId ShardKey => UserId;
}
