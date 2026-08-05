namespace ActualChat.Notifications;

public interface IApnsClient
{
    Task SendPushToTalkWake(
        ChatId chatId,
        Moment startedAt,
        string chatTitle,
        IReadOnlyCollection<Symbol> deviceIds,
        CancellationToken cancellationToken);
}
