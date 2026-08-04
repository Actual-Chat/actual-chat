namespace ActualChat;

[DataContract, MessagePackObject(true)]
public partial record AvatarChangedEvent(
    [property: DataMember] AvatarFull Avatar,
    [property: DataMember] AvatarFull? OldAvatar,
    [property: DataMember] ChangeKind ChangeKind
) : EventCommand, IHasShardKey<UserId>
{
    [IgnoreDataMember, IgnoreMember]
    public UserId ShardKey => Avatar.UserId;
}
