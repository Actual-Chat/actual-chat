using ActualChat.MLSearch.Documents;
using ActualChat.MLSearch.Engine.OpenSearch.Extensions;
using OpenSearch.Client;

namespace ActualChat.MLSearch.Engine.OpenSearch.Indexing;

internal sealed class IndexedDocuments(
    IOpenSearchClient openSearch,
    OpenSearchNames openSearchNames,
    ILogger<IndexedDocuments> log)
{
    public async Task Save<TDocument, TKey>(
        string indexName,
        IReadOnlyCollection<TDocument>? updatedDocuments,
        IReadOnlyCollection<TKey>? deletedDocumentIds,
        CancellationToken cancellationToken = default)
        where TDocument : class, IHasId<TKey>, IHasRoutingKey<TKey>
        where TKey : struct, ISymbolIdentifier<TKey>
    {
        var updatedCount = updatedDocuments?.Count ?? 0;
        var deletedCount = deletedDocumentIds?.Count ?? 0;
        var changedCount = updatedCount + deletedCount;
        if (changedCount == 0)
            return;

        var deletedSids = deletedDocumentIds?.Select(x => x.Value).ToArray() ?? [];
        log.LogInformation("Saving {DocumentType} documents to opensearch index #{IndexName}: +{UpdatedCount} -{RemovedCount}", typeof(TDocument).Name, indexName, updatedCount, deletedCount);
        var result = await openSearch
            .BulkAsync(r => r
                    .IndexMany(updatedDocuments, (op, x) => op.Index(indexName))
                    .DeleteMany<TDocument>(deletedSids,
                        (op, id) => op.Index(indexName).Routing(TDocument.GetRoutingKey(TKey.Parse(id)))),
                cancellationToken
            )
            .ConfigureAwait(false);
        log.LogErrors(result);
        result.AssertSuccess();
    }

    public async Task UpsertPartially<TDocument, TPartialDocument, TKey>(
        string indexName,
        IReadOnlyCollection<TDocument>? updatedDocuments,
        CancellationToken cancellationToken = default)
        where TPartialDocument : class, IHasId<TKey>
        where TDocument : class, TPartialDocument
        where TKey : struct, ISymbolIdentifier<TKey>
    {
        var updatedCount = updatedDocuments?.Count ?? 0;
        if (updatedCount == 0)
            return;

        log.LogInformation("Upserted {DocumentType} documents to opensearch index #{IndexName}: {Count}", typeof(TDocument).Name, indexName, updatedCount);
        var result = await openSearch
            .BulkAsync(r
                    => r.UpdateMany<TDocument, TPartialDocument>(updatedDocuments,
                        (op, x) => op.Index(indexName).Upsert(x).Doc(x)),
                cancellationToken)
            .ConfigureAwait(false);
        log.LogErrors(result);
        result.AssertSuccess();
    }

    public  Task Save<TDocument, TKey>(
        Func<OpenSearchNames, string> indexNameProvider,
        IReadOnlyCollection<TDocument>? updatedDocuments,
        IReadOnlyCollection<TKey>? deletedDocumentIds,
        CancellationToken cancellationToken = default)
        where TDocument : class, IHasId<TKey>, IHasRoutingKey<TKey>
        where TKey : struct, ISymbolIdentifier<TKey>
        => Save(indexNameProvider(openSearchNames), updatedDocuments, deletedDocumentIds, cancellationToken);

    public  Task UpsertPartially<TDocument, TPartialDocument, TKey>(
        Func<OpenSearchNames, string> indexNameProvider,
        IReadOnlyCollection<TDocument>? updatedDocuments,
        CancellationToken cancellationToken = default)
        where TDocument : class, TPartialDocument
        where TPartialDocument : class, IHasId<TKey>
        where TKey : struct, ISymbolIdentifier<TKey>
        => UpsertPartially<TDocument, TPartialDocument, TKey>(indexNameProvider(openSearchNames), updatedDocuments, cancellationToken);
}
