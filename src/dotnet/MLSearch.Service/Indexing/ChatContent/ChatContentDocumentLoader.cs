using ActualChat.MLSearch.Documents;
using ActualChat.MLSearch.Engine;
using ActualChat.MLSearch.Engine.OpenSearch.Configuration;

namespace ActualChat.MLSearch.Indexing.ChatContent;

internal interface IChatContentDocumentLoader
{
    Task<IReadOnlyCollection<ChatSlice>> LoadTailAsync(
        ChatId chatId,
        ChatContentCursor cursor,
        int tailSetSize,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyCollection<ChatSlice>> LoadByEntryIdsAsync(
        IEnumerable<ChatEntryId> entryIds,
        CancellationToken cancellationToken = default);
}

internal class ChatContentDocumentLoader(
    ISearchEngine<ChatSlice> searchEngine,
    OpenSearchNamingPolicy namingPolicy
    ): IChatContentDocumentLoader
{
    private readonly string _chatIdField = string.Join('.',
        new[] {
            nameof(ChatSlice.Metadata),
            nameof(ChatSliceMetadata.ChatId),
        }.Select(namingPolicy.ConvertName));

    private readonly string _chatEntryLocalIdField = string.Join('.',
        new[] {
            nameof(ChatSlice.Metadata),
            nameof(ChatSliceMetadata.ChatEntries),
            nameof(ChatSliceEntry.LocalId),
        }.Select(namingPolicy.ConvertName));

    private readonly string _chatEntryIdField = string.Join('.',
        new[] {
            nameof(ChatSlice.Metadata),
            nameof(ChatSliceMetadata.ChatEntries),
            nameof(ChatSliceEntry.Id),
        }.Select(namingPolicy.ConvertName));

    public async Task<IReadOnlyCollection<ChatSlice>> LoadTailAsync(
        ChatId chatId, ChatContentCursor cursor, int tailSetSize, CancellationToken cancellationToken = default)
    {
        var query = new SearchQuery {
            Filters = [
                new EqualityFilter<string>(_chatIdField, chatId),
                new Int64RangeFilter(_chatEntryLocalIdField, null, new RangeBound<long>(cursor.LastEntryLocalId, true)),
            ],
            SortStatements = [
                new SortStatement(_chatEntryLocalIdField, QuerySortOrder.Descenging, MultivalueFieldMode.Max),
            ],
            Limit = tailSetSize,
        };

        var result = await searchEngine.Find(query, cancellationToken).ConfigureAwait(false);
        return result.Documents.Select(doc => doc.Document).ToList().AsReadOnly();
    }

    public async Task<IReadOnlyCollection<ChatSlice>> LoadByEntryIdsAsync(
        IEnumerable<ChatEntryId> entryIds,
        CancellationToken cancellationToken = default)
    {
        var filters =
            from id in entryIds
            select new EqualityFilter<ChatEntryId>(_chatEntryIdField, id);

        var query = new SearchQuery {
            Filters = [new OrFilter(filters)],
        };
        var result = await searchEngine.Find(query, cancellationToken).ConfigureAwait(false);
        return result.Documents.Select(doc => doc.Document).ToList().AsReadOnly();
    }
}
