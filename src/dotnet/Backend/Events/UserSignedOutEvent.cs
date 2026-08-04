namespace ActualChat;

[DataContract, MessagePackObject(true)]
public partial record UserSignedOutEvent(
    [property: DataMember] UserId UserId,
    [property: DataMember] Session Session
) : EventCommand, IHasShardKey<UserId>
{
    [IgnoreDataMember, IgnoreMember]
    public UserId ShardKey => UserId;
}
