using OpenSearch.Client;
using ActualChat.MLSearch.Engine.OpenSearch.Extensions;
using ActualChat.MLSearch.Indexing;

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

internal sealed class Sink<TDocument>(
    string docIndexName,
    IOpenSearchClient client,
    IIndexSettingsSource indexSettingsSource,
    ILogger<Sink<TDocument>> log
) : ISink<TDocument, string>
    where TDocument: class, IHasId<string>
{
    private IndexSettings? _indexSettings;
    private IndexSettings IndexSettings => _indexSettings ??= indexSettingsSource.GetSettings(docIndexName);

    private IOpenSearchClient OpenSearch => client;

    public async Task ExecuteAsync(
        IEnumerable<TDocument>? updatedDocuments,
        IEnumerable<string>? deletedDocuments,
        CancellationToken cancellationToken)
    {
        var result = await OpenSearch
            .BulkAsync(r => r
                    .IndexMany(
                        updatedDocuments,
                        (op, document) =>
                            op
                                .Pipeline(IndexSettings.IngestPipelineId)
                                .Index(IndexSettings.IndexName)
                                .Id(document.Id)
                    )
                    .DeleteMany(deletedDocuments, (op, _) => op.Index(IndexSettings.IndexName)),
                cancellationToken
            ).ConfigureAwait(false);
        log.LogErrors(result);
        result.AssertSuccess();
    }
}
