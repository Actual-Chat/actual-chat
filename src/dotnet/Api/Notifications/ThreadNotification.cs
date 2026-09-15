namespace ActualChat.Notifications;

[DataContract, MessagePackObject]
[method: SerializationConstructor]
public sealed partial record ThreadNotification(NotificationId Id, long Version = 0)
    : ChatEntryNotification(Id, Version)
{
    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, IgnoreMember]
    public override NotificationDismissMode DismissMode
        // The anchor is the parent-chat entry the thread hangs off, which the recipient has typically
        // read already - OnRead would drop this before it ever reached a device. The chat view clears
        // it instead, once that entry is actually on screen.
        => NotificationDismissMode.OnView;
    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, IgnoreMember]
    public override Moment? ExpiresAt => SentAt + Constants.Notification.ThreadLifespan;

    public static ThreadNotification New(UserId userId, ChatEntryId entryId, AuthorId? authorId = null)
        => new(NotificationId.New(userId, NotificationKind.Thread, entryId.Value)) {
            AuthorId = authorId,
        };
}
