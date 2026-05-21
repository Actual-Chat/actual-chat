namespace ActualChat.Notifications;

[DataContract, MessagePackObject]
[method: SerializationConstructor]
public sealed partial record MessageNotification(NotificationId Id, long Version = 0)
    : ChatEntryRelatedNotification(Id, Version)
{
    public static MessageNotification New(UserId userId, ChatId chatId, long entryLid = 0, AuthorId? authorId = null)
        => new(NotificationId.New(userId, NotificationKind.Message, chatId.Value)) {
            EntryLid = entryLid,
            AuthorId = authorId,
        };
}
