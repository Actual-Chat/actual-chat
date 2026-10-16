namespace ActualChat.Notifications;

/// <summary>
/// Pending join requests of a chat or Place, for its owners and moderators. One per chat,
/// re-sent with the current count, and dismissed for all of them once none is left.
/// </summary>
[DataContract, MessagePackObject]
[method: SerializationConstructor]
public sealed partial record JoinRequestNotification(NotificationId Id, long Version = 0)
    : ChatNotification(Id, Version)
{
    [DataMember(Order = 9), Key(9)] public int PendingCount { get; init; }

    public static JoinRequestNotification New(UserId userId, ChatId chatId, int pendingCount)
        => new(NewId(userId, chatId)) { PendingCount = pendingCount };

    public static NotificationId NewId(UserId userId, ChatId chatId)
        => NotificationId.New(userId, NotificationKind.JoinRequest, chatId.Value);
}
