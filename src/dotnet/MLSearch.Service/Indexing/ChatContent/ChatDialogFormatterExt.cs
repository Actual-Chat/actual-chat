using ActualChat.Chat;
using Cysharp.Text;

namespace ActualChat.MLSearch.Indexing.ChatContent;

public static class ChatDialogFormatterExt
{
    public static async Task<string> BuildUpDialog(this IChatDialogFormatter chatDialogFormatter, IEnumerable<ChatEntry> chatEntries)
    {
        using var sb = ZString.CreateStringBuilder();
        ChatEntry? prevChatEntry = null;
        foreach (var chatEntry in chatEntries) {
            if (sb.Length > 0)
                sb.AppendLine();
            var entryText = await chatDialogFormatter.EntryToText(chatEntry, prevChatEntry).ConfigureAwait(false);
            sb.Append(entryText);
        }
        return sb.ToString();
    }
}
