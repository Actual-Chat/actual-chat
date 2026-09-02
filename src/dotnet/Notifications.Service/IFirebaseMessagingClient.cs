namespace ActualChat.Notifications;

public interface IFirebaseMessagingClient
{
    // Takes the whole active set, not just its count: the push carries the badge and, when it
    // fits, the active tags a client prunes its stale banners against.
    Task SendMessage(
        Notification notification,
        IReadOnlyCollection<Symbol> deviceIds,
        bool? enableDataCollection,
        UserNotificationInfo info,
        bool isSilent,
        CancellationToken cancellationToken);

    // Returns the dismissals FCM accepted: one it rejected stays owed, so the converge flow retries
    // it instead of losing the banner it was supposed to close.
    Task<IReadOnlyCollection<PendingDismissal>> SendDismissal(
        IReadOnlyCollection<PendingDismissal> dismissals,
        IReadOnlyCollection<Symbol> deviceIds,
        int badgeCount,
        CancellationToken cancellationToken);

    // iOS only, and deliberately separate from the dismissal it accompanies: the badge rides an
    // alert push, which lands even when the background push carrying the removal is throttled.
    Task SendBadge(
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
