namespace ActualChat.Notifications;

[DataContract, MessagePackObject]
[method: SerializationConstructor]
public sealed partial record ReactionNotification(NotificationId Id, long Version = 0)
    : ChatEntryNotification(Id, Version)
{
    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, IgnoreMember]
    public override NotificationDismissMode DismissMode
        // The anchor entry is the recipient's own message, which their Read position already covers -
        // OnRead would drop this before it ever reached a device. The chat view clears it instead,
        // once the entry is actually on screen.
        => NotificationDismissMode.OnView;
    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, IgnoreMember]
    public override Moment? ExpiresAt => SentAt + Constants.Notification.ReactionLifespan;

    // Keys 9 and 10 are free within this subtype - union members serialize independently, and
    // CallNotification / ConversationNotification already reuse the same range.
    [DataMember(Order = 9), Key(9)]
    public ApiArray<AuthorId> AuthorIds { get; init; }
    [DataMember(Order = 10), Key(10)]
    public ApiArray<Emoji> Emojis { get; init; }

    public static ReactionNotification New(UserId userId, ChatEntryId entryId, AuthorId? authorId = null)
        // AuthorIds/Emojis are left empty here and filled at the send site: ApiArray is
        // reference-compared, so a fresh array from a factory breaks record equality on a roundtrip.
        => new(NotificationId.New(userId, NotificationKind.Reaction, entryId.Value)) {
            AuthorId = authorId,
        };

    public override Notification MergeWith(Notification? existing)
    {
        if (existing is not ReactionNotification e)
            return base.MergeWith(existing);
        if (e.AuthorIds.Count >= Constants.Notification.MaxReactionAuthors)
            return e;

        var authorIds = e.AuthorIds;
        foreach (var authorId in AuthorIds)
            if (!authorIds.Contains(authorId))
                authorIds = authorIds.With(authorId);
        var emojis = e.Emojis;
        foreach (var emoji in Emojis)
            if (!emojis.Contains(emoji))
                emojis = emojis.With(emoji);
        // The queue is at-least-once, so a redelivery must return the existing instance: the notify
        // path skips the push on reference equality.
        if (authorIds.Count == e.AuthorIds.Count && emojis.Count == e.Emojis.Count)
            return e;

        // Newest-of-the-two rather than always the incoming: Title/Text/IconUrl/AuthorId are what
        // old clients render, and an out-of-order older event must not regress them.
        var newest = SentAt > e.SentAt ? this : e;
        return newest with {
            Version = e.Version,
            CreatedAt = e.CreatedAt,
            SentAt = Moment.Max(e.SentAt, SentAt),
            AuthorIds = authorIds,
            Emojis = emojis,
        };
    }
}
