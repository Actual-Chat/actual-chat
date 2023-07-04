namespace ActualChat.Chat;

public static class ChatEntryExt
{
    public static ChatEntryId? GetRepliedChatEntryId(this ChatEntry entry)
        => entry.RepliedEntryLocalId is { } repliedEntryLid
            ? new ChatEntryId(entry.Id.ChatId, entry.Id.Kind, repliedEntryLid, AssumeValid.Option)
            : null;
}
