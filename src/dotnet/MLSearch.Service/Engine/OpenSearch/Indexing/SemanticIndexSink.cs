using OpenSearch.Client;
using ActualChat.MLSearch.Engine.OpenSearch.Extensions;
using ActualChat.MLSearch.Indexing;
using Microsoft.Extensions.Options;

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
//   pipeline;
// - All deletes MUST NOT fail if document was already
//   deleted.

internal sealed class SemanticIndexSink<TDocument> : ISink<TDocument, string>, IDisposable
    where TDocument: class, IHasId<string>
{
    private readonly string _docIndexName;
    private readonly IOpenSearchClient _openSearch;
    private readonly IOptionsMonitor<SemanticIndexSettings> _indexSettingsMonitor;
    private readonly ILogger<SemanticIndexSink<TDocument>> _log;
    private readonly IDisposable? _indexSettingsChangeSubscription;
    private SemanticIndexSettings? _indexSettings;

    public SemanticIndexSink(
        string docIndexName,
        IOpenSearchClient openSearch,
        IOptionsMonitor<SemanticIndexSettings> indexSettingsMonitor,
        ILogger<SemanticIndexSink<TDocument>> log
    )
    {
        _docIndexName = docIndexName;
        _openSearch = openSearch;
        _indexSettingsMonitor = indexSettingsMonitor;
        _log = log;
        _indexSettingsChangeSubscription = _indexSettingsMonitor.OnChange((_, indexName) => {
            if (string.Equals(indexName, _docIndexName, StringComparison.Ordinal)) {
                _indexSettings = null;
            }
        });
    }

    private SemanticIndexSettings IndexSettings => _indexSettings ??= _indexSettingsMonitor.Get(_docIndexName);

    void IDisposable.Dispose() => _indexSettingsChangeSubscription?.Dispose();

    public async Task ExecuteAsync(
        IReadOnlyCollection<TDocument>? updatedDocuments,
        IReadOnlyCollection<string>? deletedDocuments,
        CancellationToken cancellationToken = default)
    {
        var changeCount = updatedDocuments?.Count + deletedDocuments?.Count;
        if (changeCount is null || changeCount == 0) {
            return;
        }
        var result = await _openSearch
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
        _log.LogErrors(result);
        result.AssertSuccess();
    }
}
