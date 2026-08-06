namespace ActualChat.Chat.ML;

internal sealed class ChatDialogFormatter(IAuthorNameRetriever authorNameRetriever) : IChatDialogFormatter
{
    private static readonly TimeSpan BlockStartTimeGap = TimeSpan.FromSeconds(120);

    public async Task<string> EntriesToText(IEnumerable<ChatEntrySlim> chatEntries, ChatDialogFormatterOptions? options = null)
    {
        options ??= ChatDialogFormatterOptions.Default;
        var sb = ActualLab.Text.StringBuilderExt.Acquire();
        ChatEntrySlim? prevChatEntry = null;
        foreach (var chatEntry in chatEntries) {
            if (sb.Length > 0)
                sb.AppendLine();
            var entryText = await EntryToText(chatEntry, prevChatEntry, options).ConfigureAwait(false);
            sb.Append(entryText);
        }
        return sb.ToStringAndRelease();
    }

    public async Task<string> EntryToText(ChatEntrySlim entry, ChatEntrySlim? prevChatEntry, ChatDialogFormatterOptions? options = null)
    {
        options ??= ChatDialogFormatterOptions.Default;
        var showAuthor = options.DisplayAuthorPerEntry;
        if (!showAuthor) {
            var isBlockStart = IsBlockStart(prevChatEntry, entry);
            var isReply = entry.RepliedEntryLid is not null;
            showAuthor = isBlockStart || isReply;
        }
        var text = await ContentToText(entry.Content).ConfigureAwait(false);
        if (!showAuthor)
            return text;

        var authorName = await GetAuthorName(entry.AuthorId).ConfigureAwait(false);
        var timestamp = entry.BeginsAt.ToDateTime();
        var sTimestamp = $"{timestamp.ToShortDateString()} at {timestamp.ToShortTimeString()}";

        var sb = ActualLab.Text.StringBuilderExt.Acquire();
        if (options.UseSquareBracketsFormat) {
            sb.Append('[');
            sb.Append(authorName);
            if (options.AddAuthorId) {
                sb.Append('|');
                sb.Append(entry.AuthorId.LocalId);
            }
            sb.Append("] ");
            if (options.DisplayTimestamp) {
                sb.Append('[');
                sb.Append(sTimestamp);
                sb.Append("] ");
            }
            sb.Append(": ");
        }
        else {
            sb.Append(authorName);
            if (options.DisplayTimestamp) {
                sb.AppendLine();
                sb.AppendLine(sTimestamp);
            }
            else
                sb.Append(": ");
        }
        sb.Append(text);
        return sb.ToStringAndRelease();
    }

    private Task<string> GetAuthorName(AuthorId authorId)
        => authorNameRetriever.GetAuthorName(authorId);

    private static Task<string> ContentToText(string markup)
        => Task.FromResult(markup); // TODO: add markup parsing

    private static bool IsBlockStart(ChatEntrySlim? prevEntry, ChatEntrySlim entry)
    {
        if (prevEntry == null)
            return true;
        if (prevEntry.AuthorId != entry.AuthorId)
            return true;

        var prevEndsAt = prevEntry.EndsAt ?? prevEntry.BeginsAt;
        return entry.BeginsAt - prevEndsAt >= BlockStartTimeGap;
    }
}
