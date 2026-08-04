namespace ActualChat;

[DataContract, MessagePackObject(true)]
public partial record UserSignedInEvent(
    [property: DataMember] UserId UserId,
    [property: DataMember] Session Session
) : EventCommand, IHasShardKey<UserId>
{
    [IgnoreDataMember, IgnoreMember]
    public UserId ShardKey => UserId;
}
