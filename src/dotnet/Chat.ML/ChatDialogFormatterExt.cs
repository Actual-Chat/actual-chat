namespace ActualChat.Chat.ML;

public static class ChatDialogFormatterExt
{
    public static Task<string> EntriesToText(this IChatDialogFormatter dialogFormatter, IEnumerable<ChatEntry> chatEntries, ChatDialogFormatterOptions? options = null)
        => dialogFormatter.EntriesToText(chatEntries.Select(c => new ChatEntrySlim(c)), options);

    public static Task<string> EntryToText(
        this IChatDialogFormatter dialogFormatter,
        ChatEntry entry,
        ChatEntry? prevChatEntry,
        ChatDialogFormatterOptions? options = null)
        => dialogFormatter.EntryToText(new ChatEntrySlim(entry),
            prevChatEntry is not null ? new ChatEntrySlim(prevChatEntry) : null,
            options);
}
