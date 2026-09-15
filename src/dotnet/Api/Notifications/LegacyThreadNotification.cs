namespace ActualChat.Notifications;

// The pre-2026.09 shape of ThreadNotification: keyed per parent chat and anchored at entry 0, so
// nothing but "Dismiss all" ever retired one. Kept on its union tag only so stored rows still
// deserialize; the expiry drains them. Nothing creates it - delete it in the next release.
[DataContract, MessagePackObject]
[method: SerializationConstructor]
public sealed partial record LegacyThreadNotification(NotificationId Id, long Version = 0)
    : ChatEntryRelatedNotification(Id, Version)
{
    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, IgnoreMember]
    public override Moment? ExpiresAt => SentAt + Constants.Notification.ThreadLifespan;
}
