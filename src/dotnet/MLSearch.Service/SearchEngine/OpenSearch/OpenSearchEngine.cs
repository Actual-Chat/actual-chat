
using ActualChat.MLSearch.ApiAdapters;
using OpenSearch.Client;

namespace ActualChat.MLSearch.SearchEngine.OpenSearch;

internal class OpenSearchEngine(IOpenSearchClient openSearch, OpenSearchClusterSettings settings, ILoggerSource loggerSource) : ISearchEngine
{
    private const string EmbeddingFieldName = "event_dense_embedding";
    private ILogger? _log;
    private ILogger Log => _log ??= loggerSource.GetLogger(GetType());

    public async Task<VectorSearchResult> Find(VectorSearchQuery query, CancellationToken cancellationToken)
    {
        var searchRequest = BuildSearchRequest(settings, query);

        var response = await openSearch.SearchAsync<IndexedDocument>(searchRequest, cancellationToken).ConfigureAwait(false);

        if (!response.IsValid) {
            throw new InvalidOperationException(response.DebugInformation);
        }

        var documents = (
                from hit in response.Hits
                select new VectorSearchRankedDocument(hit.Score, hit.Source)
            ).ToList().AsReadOnly();

        return new VectorSearchResult(documents);

        static ISearchRequest BuildSearchRequest(OpenSearchClusterSettings settings, VectorSearchQuery query)
        {
            // TODO: Add metadata filtering
            var metadataFilters = Array.Empty<QueryContainer>();
            var queries = new List<QueryContainer> { new QueryContainerDescriptor<IndexedDocument>()
                .ScriptScore(scoredQuery => scoredQuery
                    .Query(q => q.Raw(
                        $$"""
                        {
                            "neural": {
                                "{{EmbeddingFieldName}}": {
                                    "query_text": "{{query.FreeTextFilter}}",
                                    "model_id": "{{settings.ModelId}}",
                                    "k": 100
                                }
                            }
                        }
                        """))
                    .Script(script => script.Source("_score * 1.5"))),
            };

            foreach (var keyword in query.Keywords) {
                queries.Add(new QueryContainerDescriptor<IndexedDocument>()
                    .ScriptScore(scoredQuery => scoredQuery
                        .Query(q => q.Match(m => m.Field(f => f.Text).Query(keyword)))
                        .Script(script => script.Source("_score * 1.7")))
                    );
            }

            return new SearchDescriptor<IndexedDocument>()
                .Index(settings.IntoSearchIndexId())
                .Source(src => src.Excludes(excl => excl.Field(EmbeddingFieldName)))
                .Query(query => query
                    .Bool(bool_query => bool_query
                        .Filter(metadataFilters)
                        .Should(queries.ToArray())));
        }
    }

    public async Task Ingest(IndexedDocument document, CancellationToken cancellationToken)
        // TODO: support bulk api
        => await openSearch.IndexAsync(
            document,
            e=>e.Index(settings.IntoSearchIndexId()),
            cancellationToken
        ).ConfigureAwait(true);
}
