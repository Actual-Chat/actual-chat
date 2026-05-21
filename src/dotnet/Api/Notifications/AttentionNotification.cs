namespace ActualChat.Notifications;

[DataContract, MessagePackObject]
[method: SerializationConstructor]
public sealed partial record AttentionNotification(NotificationId Id, long Version = 0)
    : ChatEntryNotification(Id, Version)
{
    public static AttentionNotification New(UserId userId, ChatEntryId entryId, AuthorId? authorId = null)
        => new(NotificationId.New(userId, NotificationKind.Attention, entryId.Value)) {
            AuthorId = authorId,
        };
}
