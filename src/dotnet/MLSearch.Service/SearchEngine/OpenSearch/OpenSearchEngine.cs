
using ActualChat.MLSearch.ApiAdapters;
using ActualChat.Redis;
using OpenSearch.Client;

namespace ActualChat.MLSearch.SearchEngine.OpenSearch;

internal class OpenSearchEngine(IOpenSearchClient openSearch, OpenSearchClusterSettings settings, ILoggerSource loggerSource) : ISearchEngine
{
    private ILogger? _log;
    private ILogger Log => _log ??= loggerSource.GetLogger(GetType());

    private OpenSearchClusterSettings Settings { get; } = settings;
    private IOpenSearchClient OpenSearchClient { get; } = openSearch;

    public Task<VectorSearchResult> Find(VectorSearchQuery query, CancellationToken cancellationToken)
    {
        // Executes search over vector database
        // Returns ranked list of documents as a result
        throw new NotImplementedException();
    }

    public async Task Ingest(IndexedDocument document, CancellationToken cancellationToken)
        // TODO: support bulk api
        => await OpenSearchClient.IndexAsync(
            document,
            e=>e.Index(Settings.IntoSearchIndexId()),
            cancellationToken
        ).ConfigureAwait(true);
}
