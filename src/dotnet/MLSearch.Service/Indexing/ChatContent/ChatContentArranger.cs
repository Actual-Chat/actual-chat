using ActualChat.Chat;
using ActualChat.MLSearch.Documents;

namespace ActualChat.MLSearch.Indexing.ChatContent;

internal interface IChatContentArranger
{
    IAsyncEnumerable<SourceEntries> ArrangeAsync(IReadOnlyCollection<ChatEntry> bufferedEntries, IReadOnlyCollection<ChatSlice> tailDocuments, CancellationToken cancellationToken);
}

internal sealed class ChatContentArranger(
    IChatsBackend chatsBackend
) : IChatContentArranger
{
    public int MaxEntriesPerDocument { get; init; } = 3;

    public async IAsyncEnumerable<SourceEntries> ArrangeAsync(
        IReadOnlyCollection<ChatEntry> bufferedEntries,
        IReadOnlyCollection<ChatSlice> tailDocuments,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        // TODO: for now we just group buffered messages by three into a document
        // but in future we want to select document depending on the content
        List<ChatEntry>? tailEntries = null;
        foreach (var entry in bufferedEntries) {
            if (string.IsNullOrWhiteSpace(entry.Content)) {
                if (tailEntries?.Count > 0) {
                    yield return new SourceEntries(null, null, tailEntries);
                    tailEntries = [];
                }
                continue;
            }
            tailEntries ??= [.. await LoadTailEntries(tailDocuments, cancellationToken).ConfigureAwait(false)];
            tailEntries.Add(entry);
            if (tailEntries.Count == MaxEntriesPerDocument) {
                yield return new SourceEntries(null, null, tailEntries);
                tailEntries = [];
            }
        }

        if (tailEntries?.Count > 0) {
            yield return new SourceEntries(null, null, tailEntries);
        }
    }

    private async ValueTask<IReadOnlyList<ChatEntry>> LoadTailEntries(
        IEnumerable<ChatSlice> tailDocuments, CancellationToken cancellationToken)
    {
        var tailEntryIds = tailDocuments
            .Where(doc => doc.Metadata.ChatEntries.Length < MaxEntriesPerDocument)
            .Take(1)
            .SelectMany(doc => doc.Metadata.ChatEntries)
            .Select(e => e.Id);
        return await chatsBackend.GetEntries(tailEntryIds, true, cancellationToken).ConfigureAwait(false);
    }
}
