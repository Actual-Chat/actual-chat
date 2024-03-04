using ActualChat.MLSearch.ApiAdapters;
using OpenSearch.Client;
using ActualChat.MLSearch.Documents;
using ActualChat.MLSearch.Engine.OpenSearch.Extensions;

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
internal class Sink<TUpdateDocument, TDeleteDocument>(
    IOpenSearchClient client,
    string ingestPipelineId,
    IndexName indexName,
    Func<TUpdateDocument, ChatSlice> intoUpdate,
    Func<TDeleteDocument, Id> intoDelete,
    ILoggerSource loggerSource
)
{
    private ILogger? _log;
    private ILogger Log => _log ??= loggerSource.GetLogger(GetType());

    private IOpenSearchClient OpenSearch => client;

    public virtual async Task Execute(
        IEnumerable<TUpdateDocument>? updatedDocuments,
        IEnumerable<TDeleteDocument>? deletedDocuments,
        CancellationToken cancellationToken)
    {
        var updates = (updatedDocuments ?? Array.Empty<TUpdateDocument>())
            .Select(intoUpdate)
            .ToList();
        var deletes = (deletedDocuments ?? Array.Empty<TDeleteDocument>())
            .Select(intoDelete)
            .ToList();
        var result = await OpenSearch
            .BulkAsync(r => r
                    .IndexMany(
                        updates,
                        (op, document) =>
                            op
                                .Pipeline(ingestPipelineId)
                                .Index(indexName)
                                .Id(document.Id())
                    )
                    .DeleteMany(deletes, (op, _) => op.Index(indexName)),
                cancellationToken
            ).ConfigureAwait(false);
        Log.LogErrors(result);
        result.AssertSuccess();
    }
}
