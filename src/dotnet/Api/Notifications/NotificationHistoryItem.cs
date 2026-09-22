namespace ActualChat.Notifications;

/// <summary>
/// One row of a user's notification log: what a <see cref="Notification"/> looked like when it
/// fired, kept after the active set has dropped it.
/// </summary>
[DataContract, MessagePackObject]
public sealed partial record NotificationHistoryItem(
    [property: DataMember(Order = 0), Key(0)] long Seq,
    [property: DataMember(Order = 1), Key(1)] NotificationKind Kind)
{
    [DataMember(Order = 2), Key(2)] public Moment SentAt { get; init; }
    [DataMember(Order = 3), Key(3)] public ChatId? ChatId { get; init; }
    [DataMember(Order = 4), Key(4)] public ChatEntryId? EntryId { get; init; }
    [DataMember(Order = 5), Key(5)] public AuthorId? AuthorId { get; init; }
    [DataMember(Order = 6), Key(6)] public string Title { get; init; } = "";
    [DataMember(Order = 7), Key(7)] public string Text { get; init; } = "";

    public static bool IsLoggedKind(NotificationKind kind)
        // Message (a row per incoming message in every subscribed chat) and SpeechStarted are
        // chat traffic, not something addressed to the user, so they never reach the log.
        => kind is NotificationKind.Mention
            or NotificationKind.Reply
            or NotificationKind.Reaction
            or NotificationKind.Attention
            or NotificationKind.Thread
            or NotificationKind.Invitation
            or NotificationKind.Conversation
            or NotificationKind.IncomingCall;
}
