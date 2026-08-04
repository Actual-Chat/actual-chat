namespace ActualChat;

[DataContract, MessagePackObject(true)]
public partial record ReadPositionChangedEvent(
    [property: DataMember] UserId UserId,
    [property: DataMember] ChatId ChatId,
    [property: DataMember] long EntryLid
) : EventCommand, IHasShardKey<UserId>
{
    [IgnoreDataMember, IgnoreMember]
    public UserId ShardKey => UserId;
}
