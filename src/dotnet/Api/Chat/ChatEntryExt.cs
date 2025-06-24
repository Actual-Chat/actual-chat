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
            LinkPreviews = src.LinkPreviews,
        };

    public static Moment GetEndsAt(this ChatEntry entry)
        => entry.EndsAt ?? entry.BeginsAt;

    public static bool SupportsLanguageDetection([NotNullWhen(true)] this ChatEntry? entry)
    {
        if (!entry.SupportsTranslation(false))
            return false;

        // languages are already saved for transcribed messages
        return entry is { HasAudioEntry: false, HasVideoEntry: false };
    }

    public static bool SupportsTranslation([NotNullWhen(true)] this ChatEntry? entry, bool isForStreaming)
    {
        if (entry is null)
            return false;

        if (entry.IsSystemEntry || entry.Kind != ChatEntryKind.Text)
            return false;

        if (isForStreaming)
            return true;

        if (entry.IsStreaming)
            return false;

        if (entry.Content.IsNullOrEmpty())
            return false;

        return entry.Content.Any(char.IsLetter);
    }
}
