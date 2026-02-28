namespace ActualChat.Chat.ML;

public interface IChatDialogFormatter
{
    Task<string> EntriesToText(IEnumerable<ChatEntrySlim> chatEntries, ChatDialogFormatterOptions? options = null);
    Task<string> EntryToText(ChatEntrySlim entry, ChatEntrySlim? prevChatEntry, ChatDialogFormatterOptions? options = null);
}
