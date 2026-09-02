namespace ActualChat.Notifications;

public interface IApnsClient
{
    Task SendPttWake(
        ChatId chatId,
        Moment startedAt,
        string chatTitle,
        IReadOnlyCollection<Symbol> deviceIds,
        CancellationToken cancellationToken);

    Task SendCallRing(
        ConversationId conversationId,
        AuthorId caller,
        string callerName,
        bool hasVideo,
        IReadOnlyCollection<Symbol> deviceIds,
        CancellationToken cancellationToken);
}
