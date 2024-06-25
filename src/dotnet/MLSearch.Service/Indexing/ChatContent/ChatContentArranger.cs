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
    public async IAsyncEnumerable<SourceEntries> ArrangeAsync(
        IReadOnlyCollection<ChatEntry> bufferedEntries,
        IReadOnlyCollection<ChatSlice> tailDocuments,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        // TODO: for now we just group buffered messages by three into a document
        // but in future we want to select document depending on the content
        var tailEntryIds = tailDocuments
            .Where(doc => doc.Metadata.ChatEntries.Length < 3)
            .Take(1)
            .SelectMany(doc => doc.Metadata.ChatEntries)
            .Select(e => e.Id);
        var tailEntries = new List<ChatEntry>(
            await LoadByIdsAsync(tailEntryIds, cancellationToken).ConfigureAwait(false));
        foreach (var entry in bufferedEntries) {
            tailEntries.Add(entry);
            if (tailEntries.Count == 3) {
                yield return new SourceEntries(null, null, [.. tailEntries]);
                tailEntries.Clear();
            }
        }

        if (tailEntries.Count > 0) {
            yield return new SourceEntries(null, null, tailEntries);
        }
    }

    private ValueTask<IReadOnlyList<ChatEntry>> LoadByIdsAsync(IEnumerable<ChatEntryId> entryIds, CancellationToken cancellationToken)
        => chatsBackend.GetEntries(entryIds, true, cancellationToken);

}
