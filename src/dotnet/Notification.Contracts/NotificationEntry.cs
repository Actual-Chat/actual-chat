namespace ActualChat.Notification;

public record NotificationEntry(
    Symbol Id,
    NotificationType Type,
    string Title,
    string Content,
    string IconUrl,
    Moment NotificationTime
    ) : IHasId<Symbol>, IRequirementTarget
{
    public ChatNotificationEntry? Chat { get; init; }
    public MessageNotificationEntry? Message { get; init; }
}

public record MessageNotificationEntry(Symbol ChatId, long EntryId, string AuthorId);

public record ChatNotificationEntry(Symbol ChatId);
