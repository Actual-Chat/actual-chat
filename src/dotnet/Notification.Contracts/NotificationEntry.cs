namespace ActualChat.Notification;

public record NotificationEntry(string Title, string Content, Moment NotificationTime);

public record UserNotificationEntry(string UserId, string Title, string Content, Moment NotificationTime)
    : NotificationEntry(Title, Content, NotificationTime);

public record TopicNotificationEntry(string TopicId, string Title, string Content, Moment NotificationTime)
    : NotificationEntry(Title, Content, NotificationTime);
