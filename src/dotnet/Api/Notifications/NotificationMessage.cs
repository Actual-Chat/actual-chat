namespace ActualChat.Notifications;
/// <summary>
/// One message inside a coalesced chat notification: a display-ready snapshot (author name
/// resolved at send time) kept in <see cref="ChatEntryRelatedNotification.RecentMessages"/>.
/// </summary>
[DataContract, MessagePackObject]
public sealed partial record NotificationMessage : ISanitized
{
    // Nullable to match ChatNotification.AuthorId: a message synthesized while upgrading a
    // pre-RecentMessages blob has no author to attribute it to.
    [DataMember(Order = 0), Key(0)]
    public AuthorId? AuthorId { get; init; }
    [DataMember(Order = 1), Key(1)]
    public string AuthorName {
        get => Sanitizer.MaybeSanitize<Sanitizers.PrefixAndLengthHint>(field); init;
    } = "";
    [DataMember(Order = 2), Key(2)]
    public string Text {
        get => Sanitizer.MaybeSanitize<Sanitizers.PrefixAndLengthHint>(field); init;
    } = "";
    [DataMember(Order = 3), Key(3)]
    public long EntryLid { get; init; }
    [DataMember(Order = 4), Key(4)]
    public Moment SentAt { get; init; }
    public static NotificationMessage New(
        AuthorId? authorId, string authorName, string text,
        long entryLid, Moment sentAt)
        => new() {
            AuthorId = authorId,
            AuthorName = authorName,
            Text = Truncate(text),
            EntryLid = entryLid,
            SentAt = sentAt,
        };
    // Private methods
    private static string Truncate(string text)
        => text.Length <= Constants.Notification.MaxRecentMessageTextLength
            ? text
            : text[..(Constants.Notification.MaxRecentMessageTextLength - 1)] + "…";
}
