namespace ActualChat;

[DataContract, MessagePackObject(true)]
public partial record AccountChangedEvent(
    [property: DataMember] AccountFull Account,
    [property: DataMember] AccountFull? OldAccount,
    [property: DataMember] ChangeKind ChangeKind
) : EventCommand, IHasShardKey<UserId>
{
    [IgnoreDataMember, IgnoreMember]
    public UserId ShardKey => Account.Id;
}
