using ActualChat.MLSearch.Documents;
using ActualChat.MLSearch.Engine;

namespace ActualChat.MLSearch.Indexing.ChatContent;

internal interface IDocumentLoader
{
    Task<IReadOnlyCollection<ChatSlice>> LoadTailAsync(ChatEntryCursor cursor, CancellationToken cancellationToken);

    Task<IReadOnlyCollection<ChatSlice>> LoadByEntryIdsAsync(
        IEnumerable<ChatEntryId> entryIds,
        CancellationToken cancellationToken);
}

internal class DocumentLoader(ISearchEngine<ChatSlice> searchEngine): IDocumentLoader
{
    public async Task<IReadOnlyCollection<ChatSlice>> LoadTailAsync(ChatEntryCursor cursor, CancellationToken cancellationToken)
    {
        const string chatEntryLocalIdField =
            $"{nameof(ChatSlice.Metadata)}.{nameof(ChatSliceMetadata.ChatEntries)}.{nameof(ChatSliceEntry.LocalId)}";

        var query = new SearchQuery {
            MetadataFilters = [
                new Int64RangeFilter(chatEntryLocalIdField, null, new RangeBound<long>(cursor.LastEntryLocalId, true)),
            ],
            SortStatements = [
                new SortStatement(chatEntryLocalIdField, QuerySortOrder.Descenging, MultivalueFieldMode.Max),
            ],
            Limit = 10,
        };

        var result = await searchEngine.Find(query, cancellationToken).ConfigureAwait(false);
        return result.Documents.Select(doc => doc.Document).ToList().AsReadOnly();
    }

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
