using ActualChat.MLSearch.Documents;
using ActualChat.MLSearch.Engine.OpenSearch.Extensions;
using OpenSearch.Client;

namespace ActualChat.MLSearch.Engine.OpenSearch;

internal sealed class OpenSearchEngine<TDocument>(
    string docIndexName,
    IOpenSearchClient openSearch,
    IIndexSettingsSource indexSettingsSource,
    ILogger<OpenSearchEngine<TDocument>> log)
    : ISearchEngine<TDocument>
    where TDocument : class, IHasDocId
{
    private IndexSettings? _indexSettings;
    private IndexSettings IndexSettings => _indexSettings ??= indexSettingsSource.GetSettings(docIndexName);
    public async Task<SearchResult<TDocument>> Find(SearchQuery query, CancellationToken cancellationToken)
    {
        var queryBuilder = new OpenSearchQueryBuilder(IndexSettings);
        var searchRequest = queryBuilder.Build(query);

        // TODO: Make this serialization optional
        var json = await searchRequest.ToJsonAsync(openSearch, cancellationToken).ConfigureAwait(false);

        log.LogInformation(json);

        var response = await openSearch.SearchAsync<TDocument>(searchRequest, cancellationToken).ConfigureAwait(false);

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

    public async Task Ingest(TDocument document, CancellationToken cancellationToken)
    {
        // TODO: support bulk api
        var response = await openSearch.IndexAsync(
                document,
                e => e
                    .Pipeline(IndexSettings.IngestPipelineId)
                    .Index(IndexSettings.IndexName)
                    .Id(document.Id),
                cancellationToken
            )
            .ConfigureAwait(true);

        if (!response.IsValid) {
            throw new InvalidOperationException(response.DebugInformation, response.OriginalException);
        }
    }
}
