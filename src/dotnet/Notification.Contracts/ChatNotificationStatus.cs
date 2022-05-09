namespace ActualChat.Notification;

public sealed record ChatNotificationStatus
{
    public static ChatNotificationStatus NotSubscribed { get; } = new();
    public static ChatNotificationStatus Subscribed { get; } = new() { IsSubscribed = true };

    public bool IsSubscribed { get; init; }
}
