using System.Text;
using ActualChat.Chat;

namespace ActualChat.MLSearch.Indexing.ChatContent;

public interface IChatDialogFormatter
{
    Task<string> EntryToText(ChatEntry entry, ChatEntry? prevChatEntry);
}

public class ChatDialogFormatter(IAuthorsBackend authorsBackend, bool displayTimestamp = false) : IChatDialogFormatter
{
    private static readonly TimeSpan BlockStartTimeGap = TimeSpan.FromSeconds(120);

    public async Task<string> EntryToText(ChatEntry entry, ChatEntry? prevChatEntry)
    {
        var isBlockStart = IsBlockStart(prevChatEntry, entry);
        var isReply = entry.RepliedEntryLid is not null;
        var text = await ContentToText(entry.Content).ConfigureAwait(false);
        var showAuthor = isBlockStart || isReply;
        if (!showAuthor)
            return text;

        var authorName = await GetAuthorName(entry.AuthorId).ConfigureAwait(false);
        var timestamp = entry.BeginsAt.ToDateTime();
        var sTimestamp = $"{timestamp.ToShortDateString()} at {timestamp.ToShortTimeString()}";

        var sb = new StringBuilder();
        sb.Append(authorName);
        if (displayTimestamp) {
            sb.AppendLine();
            sb.AppendLine(sTimestamp);
        }
        else {
            sb.Append(": ");
        }
        sb.Append(text);
        return sb.ToString();
    }

    private async Task<string> GetAuthorName(AuthorId authorId)
    {
        var author = await authorsBackend
            .Get(authorId.ChatId, authorId, AuthorsBackend_GetAuthorOption.Full, default)
            .ConfigureAwait(false);
        var authorName = author?.Avatar.Name ?? "author-" + authorId.LocalId;
        return authorName;
    }

    private static Task<string> ContentToText(string markup)
        => Task.FromResult(markup); // TODO: add markup parsing

    private static bool IsBlockStart(ChatEntry? prevEntry, ChatEntry entry)
    {
        if (prevEntry == null)
            return true;
        if (prevEntry.AuthorId != entry.AuthorId)
            return true;

        var prevEndsAt = prevEntry.EndsAt ?? prevEntry.BeginsAt;
        return entry.BeginsAt - prevEndsAt >= BlockStartTimeGap;
    }
}
