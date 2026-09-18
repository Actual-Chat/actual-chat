using Notification = ActualChat.Notifications.Notification;

namespace ActualChat;

[DataContract, MessagePackObject(true)]
public sealed partial record UserNotifiedEvent(
    [property: DataMember] Notification Notification
) : EventCommand, IHasShardKey
{
    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, IgnoreMember]
    public ShardKey ShardKey => Notification.UserId.ShardKey;
}
