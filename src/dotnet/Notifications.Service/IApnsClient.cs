namespace ActualChat.Notifications;

public interface IApnsClient
{
    Task SendPttWake(
        ChatId chatId,
        Moment startedAt,
        string chatTitle,
        IReadOnlyCollection<Symbol> deviceIds,
        CancellationToken cancellationToken);

    // Returns the device ids APNs accepted the ring for - the banner may only be suppressed
    // for a device whose VoIP ring actually went out.
    Task<IReadOnlySet<Symbol>> SendCallRing(
        ConversationId conversationId,
        AuthorId caller,
        string callerName,
        bool hasVideo,
        IReadOnlyCollection<Symbol> deviceIds,
        CancellationToken cancellationToken);
}
