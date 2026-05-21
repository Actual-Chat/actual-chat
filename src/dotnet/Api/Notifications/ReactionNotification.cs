namespace ActualChat.Notifications;

[DataContract, MessagePackObject]
[method: SerializationConstructor]
public sealed partial record ReactionNotification(NotificationId Id, long Version = 0)
    : ChatEntryNotification(Id, Version)
{
    public static ReactionNotification New(UserId userId, ChatEntryId entryId, AuthorId? authorId = null)
        => new(NotificationId.New(userId, NotificationKind.Reaction, entryId.Value)) {
            AuthorId = authorId,
        };
}
