namespace ActualChat.Notifications;

[DataContract, MessagePackObject]
[method: SerializationConstructor]
public sealed partial record MentionNotification(NotificationId Id, long Version = 0)
    : ChatEntryNotification(Id, Version)
{
    public static MentionNotification New(UserId userId, ChatEntryId entryId, AuthorId? authorId = null)
        => new(NotificationId.New(userId, NotificationKind.Mention, entryId.Value)) {
            AuthorId = authorId,
        };
}
