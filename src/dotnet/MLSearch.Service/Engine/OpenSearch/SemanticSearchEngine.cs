using ActualChat.MLSearch.Engine.OpenSearch.Extensions;
using ActualChat.MLSearch.Module;
using Microsoft.Extensions.Options;
using OpenSearch.Client;

namespace ActualChat.MLSearch.Engine.OpenSearch;

internal sealed class SemanticSearchEngine<TDocument> : ISearchEngine<TDocument>, IDisposable
    where TDocument : class, IHasId<string>
{
    private readonly string _docIndexName;
    private readonly IOpenSearchClient _openSearch;
    private readonly IOptionsMonitor<SemanticIndexSettings> _indexSettingsMonitor;
    private readonly IServiceCoordinator _serviceCoordinator;
    private readonly ILogger<SemanticSearchEngine<TDocument>> _log;
    private readonly IDisposable? _indexSettingsChangeSubscription;
    private SemanticIndexSettings? _indexSettings;

    public SemanticSearchEngine(
        string docIndexName,
        IOpenSearchClient openSearch,
        IOptionsMonitor<SemanticIndexSettings> indexSettingsMonitor,
        IServiceCoordinator serviceCoordinator,
        ILogger<SemanticSearchEngine<TDocument>> log)
    {
        _docIndexName = docIndexName;
        _openSearch = openSearch;
        _indexSettingsMonitor = indexSettingsMonitor;
        _serviceCoordinator = serviceCoordinator;
        _log = log;
        _indexSettingsChangeSubscription = _indexSettingsMonitor.OnChange((_, indexName) => {
            if (OrdinalEquals(indexName, _docIndexName)) {
                _indexSettings = null;
            }
        });
    }

    private SemanticIndexSettings IndexSettings => _indexSettings ??= _indexSettingsMonitor.Get(_docIndexName);

    void IDisposable.Dispose() => _indexSettingsChangeSubscription?.Dispose();

    public async Task<SearchResult<TDocument>> Find(SearchQuery query, CancellationToken cancellationToken)
        => await _serviceCoordinator.ExecuteWhenReadyAsync(ct => FindUnsafe(query, ct), cancellationToken)
            .ConfigureAwait(false);

    private async Task<SearchResult<TDocument>> FindUnsafe(SearchQuery query, CancellationToken cancellationToken)
    {
        var queryBuilder = new SemanticSearchQueryBuilder(IndexSettings);
        var searchRequest = queryBuilder.Build(query);

        // TODO: Make this serialization optional
        var json = await searchRequest.ToJsonAsync(_openSearch, cancellationToken).ConfigureAwait(false);

        _log.LogInformation(json);

        var response = await _openSearch.SearchAsync<TDocument>(searchRequest, cancellationToken).ConfigureAwait(false);

        if (!response.IsValid) {
            throw new InvalidOperationException(response.DebugInformation, response.OriginalException);
        }

        var documents = (
                from hit in response.Hits
                select new RankedDocument<TDocument>(hit.Score, hit.Source)
            ).ToList()
            .AsReadOnly();

        return new SearchResult<TDocument>(documents);
    }
}
