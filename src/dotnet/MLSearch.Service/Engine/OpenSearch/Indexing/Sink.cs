using ActualChat.Chat;
using ActualChat.MLSearch.ApiAdapters;
using OpenSearch.Client;
using ActualChat.MLSearch.Documents;
using ActualChat.MLSearch.Engine.OpenSearch.Extensions;

namespace ActualChat.MLSearch.Engine.OpenSearch.Indexing;

internal interface ISink<in TSource>
{
    Task ExecuteAsync(
        IEnumerable<TSource>? updatedDocuments,
        IEnumerable<TSource>? deletedDocuments,
        CancellationToken cancellationToken);
}

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

internal class Sink<TSource, TDocument>(
    string docIndexName,
    IOpenSearchClient client,
    IIndexSettingsSource indexSettingsSource,
    IDocumentMapper<TSource, TDocument> mapper,
    ILoggerSource loggerSource
) : ISink<TSource> where TDocument: class, IHasDocId
{
    private IndexSettings? _indexSettings;
    private IndexSettings IndexSettings => _indexSettings ??= indexSettingsSource.GetSettings(docIndexName);

    private ILogger? _log;
    private ILogger Log => _log ??= loggerSource.GetLogger(GetType());

    private IOpenSearchClient OpenSearch => client;

    public async Task ExecuteAsync(
        IEnumerable<TSource>? updatedDocuments,
        IEnumerable<TSource>? deletedDocuments,
        CancellationToken cancellationToken)
    {
        var updates = (updatedDocuments ?? Array.Empty<TSource>())
            .Select(mapper.Map)
            .ToList();
        var deletes = (deletedDocuments ?? Array.Empty<TSource>())
            .Select(mapper.MapId)
            .ToList();
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
        Log.LogErrors(result);
        result.AssertSuccess();
    }
}

internal interface IDocumentMapper<in TSource, out TDocument>
{
    TDocument Map(TSource source);
    Id MapId(TSource source);
}

internal class ChatSliceMapper : IDocumentMapper<ChatEntry, ChatSlice>
{
    public ChatSlice Map(ChatEntry source)
        => source.IntoIndexedDocument();

    public Id MapId(ChatEntry source)
        => source.IntoDocumentId();
}
