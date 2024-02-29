using ActualChat.MLSearch.ApiAdapters;
using OpenSearch.Client;
using OpenSearch.Net;

namespace ActualChat.MLSearch.SearchEngine.OpenSearch;

internal class OpenSearchEngine(IOpenSearchClient openSearch, OpenSearchClusterSettings settings, ILoggerSource loggerSource) : ISearchEngine
{
    private ILogger? _log;
    private ILogger Log => _log ??= loggerSource.GetLogger(GetType());

    public async Task<VectorSearchResult> Find(VectorSearchQuery query, CancellationToken cancellationToken)
    {
        // TODO: index id must be a parameter
        var queryBuilder = new OpenSearchQueryBuilder(settings, "chat-slice");
        var searchRequest = queryBuilder.Build(query);

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

internal interface IQueryBuilder
{
    void ApplyEqualityFilter<TValue>(EqualityFilter<TValue> equalityFilter);
    void ApplyRangeFilter(DoubleRangeFilter rangeFilter);
    void ApplyRangeFilter(Int32RangeFilter rangeFilter);
    void ApplyRangeFilter(Int64RangeFilter rangeFilter);
    void ApplyRangeFilter(DateRangeFilter rangeFilter);
}

internal class OpenSearchQueryBuilder(OpenSearchClusterSettings settings, string indexId) : IQueryBuilder
{
    private const string EmbeddingFieldName = "event_dense_embedding";

    private readonly List<QueryContainer> metadataFilters = [];

    void IQueryBuilder.ApplyEqualityFilter<TValue>(EqualityFilter<TValue> equalityFilter)
        => metadataFilters.Add(new QueryContainerDescriptor<ChatSlice>()
            .Term(query => query.Field(equalityFilter.FieldName).Value(equalityFilter.Value))
        );

    void IQueryBuilder.ApplyRangeFilter(DoubleRangeFilter rangeFilter)
    {
        if (rangeFilter.From.HasValue || rangeFilter.To.HasValue) {
            metadataFilters.Add(new QueryContainerDescriptor<ChatSlice>()
                .Range(query => {
                    query = query.Field(rangeFilter.FieldName);
                    if (rangeFilter.From is var frm && frm.HasValue && frm.Value is var fromBound) {
                        query = fromBound.Include
                            ? query.GreaterThanOrEquals(fromBound.Value)
                            : query.GreaterThan(fromBound.Value);
                    }
                    if (rangeFilter.To is var to && to.HasValue && to.Value is var toBound) {
                        query = toBound.Include
                            ? query.LessThanOrEquals(toBound.Value)
                            : query.LessThan(toBound.Value);
                    }
                    return query;
                }));
        }
    }

    void IQueryBuilder.ApplyRangeFilter(Int32RangeFilter rangeFilter)
        => ApplyRangeFilter(new Int64RangeFilter(rangeFilter.FieldName,
            (rangeFilter.From is var frm && frm.HasValue) ? new RangeBound<long>(frm.Value.Value, frm.Value.Include) : null,
            (rangeFilter.To is var to && to.HasValue) ? new RangeBound<long>(to.Value.Value, to.Value.Include) : null
        ));

    void IQueryBuilder.ApplyRangeFilter(Int64RangeFilter rangeFilter) => ApplyRangeFilter(rangeFilter);

    private void ApplyRangeFilter(Int64RangeFilter rangeFilter)
    {
        if (rangeFilter.From.HasValue || rangeFilter.To.HasValue) {
            metadataFilters.Add(new QueryContainerDescriptor<ChatSlice>()
                .LongRange(query => {
                    query = query.Field(rangeFilter.FieldName);
                    if (rangeFilter.From is var frm && frm.HasValue && frm.Value is var fromBound) {
                        query = fromBound.Include
                            ? query.GreaterThanOrEquals(fromBound.Value)
                            : query.GreaterThan(fromBound.Value);
                    }
                    if (rangeFilter.To is var to && to.HasValue && to.Value is var toBound) {
                        query = toBound.Include
                            ? query.LessThanOrEquals(toBound.Value)
                            : query.LessThan(toBound.Value);
                    }
                    return query;
                }));
        }
    }

    void IQueryBuilder.ApplyRangeFilter(DateRangeFilter rangeFilter)
    {
        if (rangeFilter.From.HasValue || rangeFilter.To.HasValue) {
            metadataFilters.Add(new QueryContainerDescriptor<ChatSlice>()
                .DateRange(query => {
                    query = query.Field(rangeFilter.FieldName);
                    if (rangeFilter.From is var frm && frm.HasValue && frm.Value is var fromBound) {
                        query = fromBound.Include
                            ? query.GreaterThanOrEquals(fromBound.Value)
                            : query.GreaterThan(fromBound.Value);
                    }
                    if (rangeFilter.To is var to && to.HasValue && to.Value is var toBound) {
                        query = toBound.Include
                            ? query.LessThanOrEquals(toBound.Value)
                            : query.LessThan(toBound.Value);
                    }
                    return query;
                }));
        }
    }

    internal ISearchRequest Build(VectorSearchQuery query)
    {
        metadataFilters.Clear();
        foreach (var metadataFiler in query.MetadataFilters) {
            metadataFiler.Apply(this);
        }
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

        return new SearchDescriptor<ChatSlice>()
            .Index(settings.IntoFullSearchIndexId(indexId))
            .Source(src => src.Excludes(excl => excl.Field(EmbeddingFieldName)))
            .Query(query => query
                .Bool(bool_query => bool_query
                    .Filter(metadataFilters.ToArray())
                    .Should(queries.ToArray())));
    }
}
