using ActualChat.MLSearch.Documents;
using ActualChat.MLSearch.Engine.OpenSearch.Extensions;
using ActualChat.MLSearch.Indexing;
using OpenSearch.Client;

namespace ActualChat.MLSearch.Engine.OpenSearch.Indexing;

internal sealed class IndexSink<TDocument, TKey>(
    string indexName,
    IOpenSearchClient openSearch,
    ILogger<IndexSink<TDocument, TKey>> log)
    : ISink<TDocument, TKey>
    where TDocument : class, IHasId<TKey>, IHasRoutingKey<TKey>
    where TKey : struct, ISymbolIdentifier<TKey>
{
    public async Task ExecuteAsync(
        IReadOnlyCollection<TDocument>? updatedDocuments,
        IReadOnlyCollection<TKey>? deletedDocumentIds,
        CancellationToken cancellationToken = default)
    {
        var changeCount = (updatedDocuments?.Count ?? 0) + (deletedDocumentIds?.Count ?? 0);
        if (changeCount == 0)
            return;

        var deletedSids = deletedDocumentIds?.Select(x => x.Value).ToArray() ?? [];
        var result = await openSearch
            .BulkAsync(r => r
                    .IndexMany(updatedDocuments, (op, _) => op.Index(indexName))
                    .DeleteMany<TDocument>(deletedSids, (op, id) => op.Index(indexName).Routing(TDocument.GetRoutingKey(TKey.Parse(id)))),
                cancellationToken
            ).ConfigureAwait(false);
        log.LogErrors(result);
        result.AssertSuccess();
    }
}
