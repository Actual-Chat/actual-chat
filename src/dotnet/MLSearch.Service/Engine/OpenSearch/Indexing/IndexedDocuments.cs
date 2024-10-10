using ActualChat.MLSearch.Documents;
using ActualChat.MLSearch.Engine.OpenSearch.Extensions;
using OpenSearch.Client;

namespace ActualChat.MLSearch.Engine.OpenSearch.Indexing;

internal sealed class IndexedDocuments(
    IOpenSearchClient openSearch,
    OpenSearchNames openSearchNames,
    ILogger<IndexedDocuments> log)
{
    public async Task Update<TDocument, TKey>(
        string indexName,
        IReadOnlyCollection<TDocument>? updatedDocuments,
        IReadOnlyCollection<TKey>? deletedDocumentIds,
        CancellationToken cancellationToken = default)
        where TDocument : class, IHasId<TKey>, IHasRoutingKey<TKey>
        where TKey : struct, ISymbolIdentifier<TKey>
    {
        var changeCount = (updatedDocuments?.Count ?? 0) + (deletedDocumentIds?.Count ?? 0);
        if (changeCount == 0)
            return;

        var deletedSids = deletedDocumentIds?.Select(x => x.Value).ToArray() ?? [];
        var result = await openSearch
            .BulkAsync(r => r
                    .IndexMany(updatedDocuments, (op, _) => op.Index(indexName))
                    .DeleteMany<TDocument>(deletedSids,
                        (op, id) => op.Index(indexName).Routing(TDocument.GetRoutingKey(TKey.Parse(id)))),
                cancellationToken
            )
            .ConfigureAwait(false);
        log.LogErrors(result);
        result.AssertSuccess();
    }

    public  Task Update<TDocument, TKey>(
        Func<OpenSearchNames, string> indexNameProvider,
        IReadOnlyCollection<TDocument>? updatedDocuments,
        IReadOnlyCollection<TKey>? deletedDocumentIds,
        CancellationToken cancellationToken = default)
        where TDocument : class, IHasId<TKey>, IHasRoutingKey<TKey>
        where TKey : struct, ISymbolIdentifier<TKey>
        => Update(indexNameProvider(openSearchNames), updatedDocuments, deletedDocumentIds, cancellationToken);
}
