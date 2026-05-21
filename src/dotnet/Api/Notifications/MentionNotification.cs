namespace ActualChat.Notifications;

[DataContract, MessagePackObject]
[method: SerializationConstructor]
public sealed partial record MentionNotification(NotificationId Id, long Version = 0)
    : ChatNotification(Id, Version)
{
    public static MentionNotification New(UserId userId, ChatId chatId, long entryLid = 0, AuthorId? authorId = null)
        => new(NotificationId.New(userId, NotificationKind.Mention, chatId.Value)) {
            EntryLid = entryLid,
            AuthorId = authorId,
        };
}
