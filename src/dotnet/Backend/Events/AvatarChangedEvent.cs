namespace ActualChat;

[DataContract, MessagePackObject(true)]
public partial record AvatarChangedEvent(
    [property: DataMember] AvatarFull Avatar,
    [property: DataMember] AvatarFull? OldAvatar,
    [property: DataMember] ChangeKind ChangeKind
) : EventCommand, IHasShardKey<UserId>
{
    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, IgnoreMember]
    public UserId ShardKey => Avatar.UserId;
}
