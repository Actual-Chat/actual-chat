
using ActualChat.MLSearch.ApiAdapters;
using OpenSearch.Client;
using OpenSearch.Net;

namespace ActualChat.MLSearch.SearchEngine.OpenSearch;

internal class OpenSearchEngine(IOpenSearchClient openSearch, OpenSearchClusterSettings settings, ILoggerSource loggerSource) : ISearchEngine
{
    private const string EmbeddingFieldName = "event_dense_embedding";
    private ILogger? _log;
    private ILogger Log => _log ??= loggerSource.GetLogger(GetType());

    public async Task<VectorSearchResult> Find(VectorSearchQuery query, CancellationToken cancellationToken)
    {
        var searchRequest = BuildSearchRequest(settings, query);

        // TODO: Make this serialization optional
        var json = await searchRequest.ToJsonAsync(openSearch, cancellationToken).ConfigureAwait(false);

        Log.LogInformation(json);

        var response = await openSearch.SearchAsync<ChatSlice>(searchRequest, cancellationToken).ConfigureAwait(false);

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
            var queries = new List<QueryContainer> { new QueryContainerDescriptor<ChatSlice>()
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
                queries.Add(new QueryContainerDescriptor<ChatSlice>()
                    .ScriptScore(scoredQuery => scoredQuery
                        .Query(q => q.Match(m => m.Field(f => f.Text).Query(keyword)))
                        .Script(script => script.Source("_score * 1.7")))
                    );
            }

            // TODO: index id must be a parameter
            return new SearchDescriptor<ChatSlice>()
                .Index(settings.IntoFullSearchIndexId("chat-slice"))
                .Source(src => src.Excludes(excl => excl.Field(EmbeddingFieldName)))
                .Query(query => query
                    .Bool(bool_query => bool_query
                        .Filter(metadataFilters)
                        .Should(queries.ToArray())));
        }
    }

    public async Task Ingest(ChatSlice document, CancellationToken cancellationToken)
        // TODO: support bulk api
        // TODO: index id must be a parameter
        => await openSearch.IndexAsync(
            document,
            e=>e.Index(settings.IntoFullSearchIndexId("chat-slice")),
            cancellationToken
        ).ConfigureAwait(true);
}

internal static class OpenSearchExtensions
{
    public static async Task<string> ToJsonAsync(this ISearchRequest searchRequest, IOpenSearchClient openSearch, CancellationToken cancellationToken)
    {
        var serializableRequest = PostData.Serializable(searchRequest);
        serializableRequest.DisableDirectStreaming = false;

        var ms = new MemoryStream();
        await serializableRequest.WriteAsync(ms, openSearch.ConnectionSettings, cancellationToken).ConfigureAwait(false);
        ms.Seek(0, SeekOrigin.Begin);
        using var reader = new StreamReader(ms);
        return await reader.ReadToEndAsync(cancellationToken).ConfigureAwait(false);
    }
}
