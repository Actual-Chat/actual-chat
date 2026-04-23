namespace ActualChat;

[DataContract, MemoryPackable(GenerateType.VersionTolerant)]
public partial record UserSignedInEvent(
    [property: DataMember, MemoryPackOrder(1)] UserId UserId,
    [property: DataMember, MemoryPackOrder(2)] Session Session
) : EventCommand, IHasShardKey<UserId>
{
    [IgnoreDataMember, MemoryPackIgnore]
    public UserId ShardKey => UserId;
}
