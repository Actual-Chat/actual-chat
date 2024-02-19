
using ActualChat.Redis;
using OpenSearch.Client;

namespace ActualChat.MLSearch.SearchEngine.OpenSearch;

internal class OpenSearchEngine(IServiceProvider services) : ISearchEngine
{
    private OpenSearchClusterSettings? _settings;
    private OpenSearchClient? _opensearch;
    private ILogger? _log;

    private OpenSearchClusterSettings Settings => _settings ??= services.GetRequiredService<OpenSearchClusterSettings>();
    private OpenSearchClient OpenSearchClient => _opensearch ??= services.GetRequiredService<OpenSearchClient>();

    public Task<VectorSearchResult> Find(VectorSearchQuery query, CancellationToken cancellationToken)
        // Executes search over vector database
        // Returns ranked list of documents as a result
        => throw new NotImplementedException();

    public async Task Ingest(IndexedDocument document, CancellationToken cancellationToken)
        // TODO: support bulk api
        => await OpenSearchClient.IndexAsync(
            document,
            e=>e.Index(Settings.IntoSearchIndexId()),
            cancellationToken
        ).ConfigureAwait(true);
}
