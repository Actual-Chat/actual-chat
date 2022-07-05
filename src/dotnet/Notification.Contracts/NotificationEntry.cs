namespace ActualChat.Notification;

public record NotificationEntry(string NotificationId, NotificationType Type, string Title, string Content, Moment NotificationTime)
{
    public ChatNotificationEntry? Chat { get; init; }
    public MessageNotificationEntry? Message { get; init; }
}

public record MessageNotificationEntry(string ChatId, long EntryId, string AuthorId);

public record ChatNotificationEntry(string ChatId);
