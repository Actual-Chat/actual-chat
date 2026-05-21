namespace ActualChat.Notifications;

[DataContract, MessagePackObject]
[method: SerializationConstructor]
public sealed partial record InvitationNotification(NotificationId Id, long Version = 0)
    : ChatNotification(Id, Version)
{
    public static InvitationNotification New(UserId userId, ChatId chatId, long entryLid = 0, AuthorId? authorId = null)
        => new(NotificationId.New(userId, NotificationKind.Invitation, chatId.Value)) {
            EntryLid = entryLid,
            AuthorId = authorId,
        };
}
