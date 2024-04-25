using ActualChat.MLSearch.Documents;
using OpenSearch.Client;

namespace ActualChat.MLSearch.Engine.OpenSearch;

internal sealed class OpenSearchQueryBuilder(IndexSettings settings) : IQueryBuilder
{
    private const string EmbeddingFieldName = "event_dense_embedding";

    private List<QueryContainer> _metadataFilters = [];

    void IQueryBuilder.ApplyOrFilter(OrFilter orFilter)
    {
        var oldMetadataFilters = _metadataFilters;
        _metadataFilters = [];

        foreach (var filter in orFilter.Filters) {
            filter.Apply(this);
        }

        oldMetadataFilters.Add(new QueryContainerDescriptor<ChatSlice>()
            .Bool(boolQuery => boolQuery.Should(_metadataFilters.ToArray())));
        _metadataFilters = oldMetadataFilters;
    }

    void IQueryBuilder.ApplyEqualityFilter<TValue>(EqualityFilter<TValue> equalityFilter)
        => _metadataFilters.Add(new QueryContainerDescriptor<ChatSlice>()
            .Term(query => query.Field(equalityFilter.FieldName).Value(equalityFilter.Value))
        );

    void IQueryBuilder.ApplyRangeFilter(DoubleRangeFilter rangeFilter)
    {
        if (rangeFilter.From.HasValue || rangeFilter.To.HasValue) {
            _metadataFilters.Add(new QueryContainerDescriptor<ChatSlice>()
                .Range(query => {
                    query = query.Field(rangeFilter.FieldName);
                    if (rangeFilter.From is { Value: var fromBound, Include: var isFromIncluded}) {
                        query = isFromIncluded
                            ? query.GreaterThanOrEquals(fromBound)
                            : query.GreaterThan(fromBound);
                    }
                    if (rangeFilter.To is { Value: var toBound , Include: var isToIncluded}) {
                        query = isToIncluded
                            ? query.LessThanOrEquals(toBound)
                            : query.LessThan(toBound);
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
            _metadataFilters.Add(new QueryContainerDescriptor<ChatSlice>()
                .LongRange(query => {
                    query = query.Field(rangeFilter.FieldName);
                    if (rangeFilter.From is { Value: var fromBound, Include: var isFromIncluded}) {
                        query = isFromIncluded
                            ? query.GreaterThanOrEquals(fromBound)
                            : query.GreaterThan(fromBound);
                    }
                    if (rangeFilter.To is { Value: var toBound , Include: var isToIncluded}) {
                        query = isToIncluded
                            ? query.LessThanOrEquals(toBound)
                            : query.LessThan(toBound);
                    }
                    return query;
                }));
        }
    }

    void IQueryBuilder.ApplyRangeFilter(DateRangeFilter rangeFilter)
    {
        if (rangeFilter.From.HasValue || rangeFilter.To.HasValue) {
            _metadataFilters.Add(new QueryContainerDescriptor<ChatSlice>()
                .DateRange(query => {
                    query = query.Field(rangeFilter.FieldName);
                    if (rangeFilter.From is { Value: var fromBound, Include: var isFromIncluded}) {
                        query = isFromIncluded
                            ? query.GreaterThanOrEquals(fromBound)
                            : query.GreaterThan(fromBound);
                    }
                    if (rangeFilter.To is { Value: var toBound , Include: var isToIncluded}) {
                        query = isToIncluded
                            ? query.LessThanOrEquals(toBound)
                            : query.LessThan(toBound);
                    }
                    return query;
                }));
        }
    }

    internal ISearchRequest Build(SearchQuery searchQuery)
    {
        var queryRoot = new SearchDescriptor<ChatSlice>()
            .Index(settings.IndexName)
            .Source(src => src.Excludes(excl => excl.Field(EmbeddingFieldName)));

        if (searchQuery.IsUnfiltered()) {
            return queryRoot
                .Sort(SortSelector)
                .Size(searchQuery.Limit);
        }

        _metadataFilters.Clear();
        foreach (var metadataFiler in searchQuery.MetadataFilters ?? Enumerable.Empty<IMetadataFilter>()) {
            metadataFiler.Apply(this);
        }
        var queries = searchQuery.FreeTextFilter is null
            ? new List<QueryContainer>()
            : [ new QueryContainerDescriptor<ChatSlice>()
                .ScriptScore(scoredQuery => scoredQuery
                    .Query(q => q.Raw(
                        $$"""
                          {
                              "neural": {
                                  "{{EmbeddingFieldName}}": {
                                      "query_text": "{{searchQuery.FreeTextFilter}}",
                                      "model_id": "{{settings.ModelId}}",
                                      "k": 100
                                  }
                              }
                          }
                          """))
                    .Script(script => script.Source("_score * 1.5"))),
            ];

        foreach (var keyword in searchQuery.Keywords ?? Enumerable.Empty<string>()) {
            queries.Add(new QueryContainerDescriptor<ChatSlice>()
                .ScriptScore(scoredQuery => scoredQuery
                    .Query(q => q.Match(m => m.Field(f => f.Text).Query(keyword)))
                    .Script(script => script.Source("_score * 1.7")))
            );
        }

        return queryRoot.Query(query => query
            .Bool(boolQuery => boolQuery
                .Filter(_metadataFilters.ToArray())
                .Should(queries.ToArray())))
            .Sort(SortSelector)
            .Size(searchQuery.Limit);

        IPromise<IList<ISort>>? SortSelector(SortDescriptor<ChatSlice> ss)
        {
            if (searchQuery.SortStatements is null || searchQuery.SortStatements.Length == 0) {
                return null;
            }
            foreach (var sortStatement in searchQuery.SortStatements) {
                ss.Field(_ => new FieldSort {
                    Field = sortStatement.Field,
                    Order = sortStatement.SortOrder == QuerySortOrder.Ascending ? SortOrder.Ascending : SortOrder.Descending,
                    Mode = sortStatement.Mode == MultivalueFieldMode.Min ? SortMode.Min : SortMode.Max,
                });
            }
            return ss;
        }
    }
}
