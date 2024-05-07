using ActualChat.MLSearch.Engine.OpenSearch.Extensions;
using Microsoft.Extensions.Options;
using OpenSearch.Client;

namespace ActualChat.MLSearch.Engine.OpenSearch;

internal sealed class OpenSearchEngine<TDocument> : ISearchEngine<TDocument>, IDisposable
    where TDocument : class, IHasId<string>
{
    private readonly string _docIndexName;
    private readonly IOpenSearchClient _openSearch;
    private readonly IOptionsMonitor<IndexSettings> _indexSettingsMonitor;
    private readonly ILogger<OpenSearchEngine<TDocument>> _log;
    private readonly IDisposable? _indexSettingsChangeSubscription;
    private IndexSettings? _indexSettings;

    public OpenSearchEngine(
        string docIndexName,
        IOpenSearchClient openSearch,
        IOptionsMonitor<IndexSettings> indexSettingsMonitor,
        ILogger<OpenSearchEngine<TDocument>> log)
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

    private IndexSettings IndexSettings => _indexSettings ??= _indexSettingsMonitor.Get(_docIndexName);

    void IDisposable.Dispose() => _indexSettingsChangeSubscription?.Dispose();

    public async Task<SearchResult<TDocument>> Find(SearchQuery query, CancellationToken cancellationToken)
    {
        var queryBuilder = new OpenSearchQueryBuilder(IndexSettings);
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
