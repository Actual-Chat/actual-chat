namespace ActualChat.Notifications;

[DataContract, MessagePackObject]
[method: SerializationConstructor]
public sealed partial record ThreadNotification(NotificationId Id, long Version = 0)
    : ChatEntryRelatedNotification(Id, Version)
{
    public static ThreadNotification New(UserId userId, ChatId chatId, long entryLid = 0, AuthorId? authorId = null)
        => new(NotificationId.New(userId, NotificationKind.Thread, chatId.Value)) {
            EntryLid = entryLid,
            AuthorId = authorId,
        };
}
