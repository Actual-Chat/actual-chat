namespace ActualChat.Chat;

public static class ChatEntryExt
{
    public static TextEntryId? GetRepliedChatEntryId(this ChatEntry entry)
        => entry.RepliedEntryLid is { } repliedEntryLid
            ? TextEntryId.New(entry.Id.ChatId, repliedEntryLid)
            : null;

    public static ChatEntry WithPopulatedValues(this ChatEntry entry, ChatEntry src)
        => entry with {
            Attachments = src.Attachments,
 #pragma warning disable CS0618 // Type or member is obsolete
            LinkPreview = src.LinkPreview,
 #pragma warning restore CS0618 // Type or member is obsolete
            LinkPreviews = src.LinkPreviews,
        };

    public static Moment GetEndsAt(this ChatEntry entry)
        => entry.EndsAt ?? entry.BeginsAt;
}
