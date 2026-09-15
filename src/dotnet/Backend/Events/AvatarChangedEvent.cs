namespace ActualChat;

[DataContract, MessagePackObject(true)]
public partial record AvatarChangedEvent(
    [property: DataMember] AvatarFull Avatar,
    [property: DataMember] AvatarFull? OldAvatar,
    [property: DataMember] ChangeKind ChangeKind
) : EventCommand, IHasShardKey
{
    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, IgnoreMember]
    public ShardKey ShardKey => Avatar.UserId.ShardKey;
}
