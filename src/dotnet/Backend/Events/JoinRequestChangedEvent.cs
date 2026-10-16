using ActualChat.Chat;

namespace ActualChat;

[DataContract, MessagePackObject(true)]
public sealed partial record JoinRequestChangedEvent(
    [property: DataMember] JoinRequest Request,
    [property: DataMember] JoinRequest? OldRequest
) : EventCommand, IHasShardKey
{
    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, IgnoreMember]
    public ShardKey ShardKey => Request.ChatId.ShardKey;
}
