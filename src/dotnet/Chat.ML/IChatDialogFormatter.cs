namespace ActualChat.Chat.ML;

public interface IChatDialogFormatter
{
    Task<string> EntriesToText(IEnumerable<TextEntry> chatEntries, ChatDialogFormatterOptions? options = null);
    Task<string> EntryToText(TextEntry entry, TextEntry? prevChatEntry, ChatDialogFormatterOptions? options = null);
}
