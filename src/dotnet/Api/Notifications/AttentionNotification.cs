namespace ActualChat.Notifications;

[DataContract, MessagePackObject]
[method: SerializationConstructor]
public sealed partial record AttentionNotification(NotificationId Id, long Version = 0)
    : ChatEntryNotification(Id, Version)
{
    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, IgnoreMember]
    public override NotificationDismissMode DismissMode
        // The anchor entry is one the recipient has typically read already - the mentioned message,
        // or the one before an "alert everyone" - so OnRead dropped the ping before it reached a
        // device. The chat view clears it instead, once the entry is actually on screen.
        => NotificationDismissMode.OnView;
    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, IgnoreMember]
    public override Moment? ExpiresAt => SentAt + Constants.Notification.AttentionLifespan;

    public static AttentionNotification New(UserId userId, ChatEntryId entryId, AuthorId? authorId = null)
        => new(NotificationId.New(userId, NotificationKind.Attention, entryId.Value)) {
            AuthorId = authorId,
        };
}
