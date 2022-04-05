namespace ActualChat.Notification;

public record NotificationEntry(string Title, string Content, Moment NotificationTime);

public record UserNotificationEntry(string UserId, string Title, string Content, Moment NotificationTime)
    : NotificationEntry(Title, Content, NotificationTime);

public record ChatNotificationEntry(string ChatId, long EntryId, string AuthorId, string Title, string Content, Moment NotificationTime)
    : NotificationEntry(Title, Content, NotificationTime);

public record TopicNotificationEntry(string Topic, string Title, string Content, Moment NotificationTime)
    : NotificationEntry(Title, Content, NotificationTime);
