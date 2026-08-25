namespace ActualChat.Notifications;

public interface IFirebaseMessagingClient
{
    Task SendMessage(
        Notification notification,
        IReadOnlyCollection<Symbol> deviceIds,
        bool? enableDataCollection,
        int badgeCount,
        bool isSilent,
        CancellationToken cancellationToken);

    Task SendDismissal(
        IReadOnlyCollection<PendingDismissal> dismissals,
        IReadOnlyCollection<Symbol> deviceIds,
        int badgeCount,
        CancellationToken cancellationToken);

    Task SendSpeechStartedWake(
        ChatId chatId,
        AuthorId authorId,
        Moment startedAt,
        IReadOnlyCollection<Symbol> deviceIds,
        CancellationToken cancellationToken);
}
