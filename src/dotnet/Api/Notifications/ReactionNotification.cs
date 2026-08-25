namespace ActualChat.Notifications;

[DataContract, MessagePackObject]
[method: SerializationConstructor]
public sealed partial record ReactionNotification(NotificationId Id, long Version = 0)
    : ChatEntryNotification(Id, Version)
{
    // The anchor entry is the recipient's own message, which their Read position already covers -
    // OnRead would drop this before it ever reached a device. The chat view clears it instead,
    // once the entry is actually on screen.
    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, IgnoreMember]
    public override NotificationDismissMode DismissMode => NotificationDismissMode.OnView;
    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, IgnoreMember]
    public override Moment? ExpiresAt => SentAt + Constants.Notification.ReactionLifespan;

    public static ReactionNotification New(UserId userId, ChatEntryId entryId, AuthorId? authorId = null)
        => new(NotificationId.New(userId, NotificationKind.Reaction, entryId.Value)) {
            AuthorId = authorId,
        };
}
