using ActualChat.MLSearch.Documents;
using ActualChat.MLSearch.Engine;

namespace ActualChat.MLSearch.Indexing.ChatContent;

internal interface IDocumentLoader
{
    Task<IReadOnlyCollection<ChatSlice>> LoadTailAsync(CancellationToken cancellationToken);

    Task<IReadOnlyCollection<ChatSlice>> LoadByEntryIdsAsync(
        IEnumerable<ChatEntryId> entryIds,
        CancellationToken cancellationToken);
}

internal class DocumentLoader(ISearchEngine<ChatSlice> searchEngine): IDocumentLoader
{
    public Task<IReadOnlyCollection<ChatSlice>> LoadTailAsync(CancellationToken cancellationToken)
        => throw new NotImplementedException();

    public async Task<IReadOnlyCollection<ChatSlice>> LoadByEntryIdsAsync(
        IEnumerable<ChatEntryId> entryIds,
        CancellationToken cancellationToken)
    {
        const string chatEntryIdField =
            $"{nameof(ChatSlice.Metadata)}.{nameof(ChatSliceMetadata.ChatEntries)}.{nameof(ChatSliceEntry.Id)}";

        var filters =
            from id in entryIds
            select new EqualityFilter<ChatEntryId>(chatEntryIdField, id);

        var query = new SearchQuery {
            MetadataFilters = [new OrFilter(filters)],
        };
        var result = await searchEngine.Find(query, cancellationToken).ConfigureAwait(false);
        return result.Documents.Select(doc => doc.Document).ToList().AsReadOnly();
    }
}
