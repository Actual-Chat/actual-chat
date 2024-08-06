using System.Text;
using ActualChat.Chat;
using ActualChat.MLSearch.Documents;

namespace ActualChat.MLSearch.Indexing.ChatContent;

internal sealed class ChatContentArranger2(
    IChatsBackend chatsBackend,
    IAuthorsBackend authorsBackend,
    FragmentContinuationSelector selector
) : IChatContentArranger
{
    public async IAsyncEnumerable<SourceEntries> ArrangeAsync(
        IReadOnlyCollection<ChatEntry> bufferedEntries,
        IReadOnlyCollection<ChatSlice> tailDocuments,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        if (bufferedEntries.Count == 0)
            yield break;

        // TODO: we want to select document depending on the content
        var builders = new List<SourceEntriesBuilder>();
        foreach (var tailDocument in tailDocuments)
            builders.Add(new SourceEntriesBuilder(tailDocument));

        // Preload document tails
        foreach (var builder in builders) {
            if (builder is { RelatedChatSlice: not null, HasInitializedEntries: false }) {
                var tailEntries = await LoadTailEntries(builder.RelatedChatSlice, cancellationToken)
                    .ConfigureAwait(false);
                builder.Entries.AddRange(tailEntries);
                builder.HasInitializedEntries = true;
                builder.Dialog = await BuildUpDialog(tailEntries).ConfigureAwait(false);
            }
        }

        foreach (var entry in bufferedEntries) {
            if (string.IsNullOrWhiteSpace(entry.Content))
                continue;
            if (entry.IsSystemEntry)
                continue;

            if (builders.Count == 0) {
                var builder = new SourceEntriesBuilder(null) {
                    HasInitializedEntries = true,
                    HasModified = true
                };
                builders.Add(builder);
                builder.Entries.Add(entry);
                builder.Dialog = await BuildUpDialog(builder.Entries).ConfigureAwait(false);
            }
            else {
                // Do we need to close any builder: have some content and last entry created earlier than 1 day from the current one.
                SourceEntriesBuilder? builderToAdd = null;
                var candidates = new List<SourceEntriesBuilder>();

                foreach (var builder in builders) {
                    var dialog = builder.Dialog;
                    var entryText = await EntryToText(entry, builder.Entries[^1]).ConfigureAwait(false);
                    dialog = dialog + Environment.NewLine + entryText;
                    builder.PossibleDialog = dialog;
                    var result = await selector.IsFragmentAboutTheSameTopic(dialog).ConfigureAwait(false);
                    if (result is { HasValue: true, Value: true })
                        candidates.Add(builder);
                }
                if (candidates.Count == 1)
                    builderToAdd = candidates[0];
                else if (candidates.Count > 1) {
                    var dialogs = candidates.Select(c => c.PossibleDialog).ToArray();
                    var index = await selector.ChooseMoreProbableDialog(dialogs).ConfigureAwait(false);
                    if (index >= 0)
                        builderToAdd = candidates[index];
                }

                if (builderToAdd != null) {
                    builderToAdd.HasModified = true;
                    builderToAdd.Entries.Add(entry);
                    builderToAdd.Dialog = builderToAdd.PossibleDialog;
                }
                else {
                    var builder = new SourceEntriesBuilder(null) {
                        HasInitializedEntries = true,
                        HasModified = true
                    };
                    builder.Entries.Add(entry);
                    builder.Dialog = await BuildUpDialog(builder.Entries).ConfigureAwait(false);
                    builders.Add(builder);
                }
            }
        }

        foreach (var builder in builders) {
            if (builder.HasModified)
                yield return new SourceEntries(null, null, builder.Entries);
        }
    }

    private async Task<string> BuildUpDialog(IReadOnlyList<ChatEntry> chatEntries)
    {
        var sb = new StringBuilder();
        ChatEntry? prevChatEntry = null;
        foreach (var chatEntry in chatEntries) {
            if (sb.Length > 0)
                sb.AppendLine();
            var entryText = await EntryToText(chatEntry, prevChatEntry).ConfigureAwait(false);
            sb.Append(entryText);
        }
        return sb.ToString();
    }

    private async Task<string> EntryToText(ChatEntry entry, ChatEntry? prevChatEntry)
    {
        var isBlockStart = IsBlockStart(prevChatEntry, entry);
        var isReply = entry.RepliedEntryLocalId is not null;
        var text = await ContentToText(entry.Content).ConfigureAwait(false);
        var showAuthor = isBlockStart || isReply;
        if (!showAuthor)
            return text;

        var authorName = await GetAuthorName(entry.AuthorId).ConfigureAwait(false);
        var timestamp = entry.BeginsAt.ToDateTime();
        var sTimestamp = $"{timestamp.ToShortDateString()} at {timestamp.ToShortTimeString()}";

        var sb = new StringBuilder();
        sb.AppendLine(authorName);
        sb.AppendLine(sTimestamp);
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

    private Task<string> ContentToText(string markup)
        => Task.FromResult(markup); // TODO: add markup parsing

    private static readonly TimeSpan BlockStartTimeGap = TimeSpan.FromSeconds(120);

    private static bool IsBlockStart(ChatEntry? prevEntry, ChatEntry entry)
    {
        if (prevEntry == null)
            return true;
        if (prevEntry.AuthorId != entry.AuthorId)
            return true;

        var prevEndsAt = prevEntry.EndsAt ?? prevEntry.BeginsAt;
        return entry.BeginsAt - prevEndsAt >= BlockStartTimeGap;
    }

    private async ValueTask<IReadOnlyList<ChatEntry>> LoadTailEntries(
        ChatSlice tailDocument, CancellationToken cancellationToken)
    {
        var tailEntryIds = tailDocument.Metadata
            .ChatEntries
            .Select(e => e.Id);
        return await chatsBackend.GetEntries(tailEntryIds, false, cancellationToken).ConfigureAwait(false);
    }

    private record SourceEntriesBuilder(ChatSlice? RelatedChatSlice)
    {
        public bool HasInitializedEntries { get; set; }
        public bool HasModified { get; set; }
        public List<ChatEntry> Entries { get; } = [];
        public string Dialog { get; set; } = "";
        public string PossibleDialog { get; set; } = "";
    }
}
