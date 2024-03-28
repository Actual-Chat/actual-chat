using OpenSearch.Client;
using ActualChat.MLSearch.Engine.OpenSearch.Extensions;
using ActualChat.MLSearch.Engine.Indexing;

namespace ActualChat.MLSearch.Engine.OpenSearch.Indexing;

// Note: Sink implementation requirements.
// Since Sink api executed on top of bulk actions
// it is possible to have a situation when some
// of the updated documents fail to sink.
// In this case it must raise an error and a client
// code MAY retry the same set of updates.
// This means the Sink implementation MUST expect
// the same update being replayed multiple times.
// What does it mean:
// - All document updated/created MUST have _id field
//   set in the request or calculated in the ingest
//   pipeline.
// - All deletes MUST NOT fail if document was already
//   deleted.

internal sealed class Sink<TSource, TDocument>(
    string docIndexName,
    IOpenSearchClient client,
    IIndexSettingsSource indexSettingsSource,
    IDocumentMapper<TSource, TDocument> mapper,
    ILogger<Sink<TSource, TDocument>> log
) : ISink<TSource, TSource> where TDocument: class, IHasId<string>
{
    private IndexSettings? _indexSettings;
    private IndexSettings IndexSettings => _indexSettings ??= indexSettingsSource.GetSettings(docIndexName);

    private IOpenSearchClient OpenSearch => client;

    public async Task ExecuteAsync(
        IEnumerable<TSource>? updatedDocuments,
        IEnumerable<TSource>? deletedDocuments,
        CancellationToken cancellationToken)
    {
        var updates = updatedDocuments?.Select(mapper.Map) ?? Enumerable.Empty<TDocument>();
        var deletes = deletedDocuments?.Select(mapper.MapId) ?? Enumerable.Empty<Id>();

        var result = await OpenSearch
            .BulkAsync(r => r
                    .IndexMany(
                        updates,
                        (op, document) =>
                            op
                                .Pipeline(IndexSettings.IngestPipelineId)
                                .Index(IndexSettings.IndexName)
                                .Id(document.Id)
                    )
                    .DeleteMany(deletes, (op, _) => op.Index(IndexSettings.IndexName)),
                cancellationToken
            ).ConfigureAwait(false);
        log.LogErrors(result);
        result.AssertSuccess();
    }
}
