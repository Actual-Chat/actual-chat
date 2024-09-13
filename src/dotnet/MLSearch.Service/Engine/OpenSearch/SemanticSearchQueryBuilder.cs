using ActualChat.MLSearch.Documents;
using ActualChat.MLSearch.Engine.OpenSearch.Configuration;
using OpenSearch.Client;

namespace ActualChat.MLSearch.Engine.OpenSearch;

internal sealed class SemanticSearchQueryBuilder(SemanticIndexSettings settings) : IQueryBuilder
{
    private const string EmbeddingFieldName = "event_dense_embedding";

    private List<QueryContainer> _queryFilters = [];
    private readonly List<QueryContainer> _queries = [];
    private readonly HashSet<string> _keywords = [];

    void IQueryBuilder.ApplyOrFilter(OrFilter orFilter)
    {
        var oldMetadataFilters = _queryFilters;
        _queryFilters = [];

        foreach (var filter in orFilter.Filters) {
            filter.Apply(this);
        }

        oldMetadataFilters.Add(new QueryContainerDescriptor<ChatSlice>()
            .Bool(boolQuery => boolQuery.Should(_queryFilters.ToArray())));
        _queryFilters = oldMetadataFilters;
    }

    void IQueryBuilder.ApplyEqualityFilter<TValue>(EqualityFilter<TValue> equalityFilter)
        => _queryFilters.Add(new QueryContainerDescriptor<ChatSlice>()
            .Term(query => query.Field(equalityFilter.FieldName).Value(equalityFilter.Value))
        );

    void IQueryBuilder.ApplyRangeFilter(DoubleRangeFilter rangeFilter)
    {
        if (rangeFilter.From.HasValue || rangeFilter.To.HasValue) {
            _queryFilters.Add(new QueryContainerDescriptor<ChatSlice>()
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
            _queryFilters.Add(new QueryContainerDescriptor<ChatSlice>()
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
            _queryFilters.Add(new QueryContainerDescriptor<ChatSlice>()
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

        if (searchQuery.Filters is null || searchQuery.Filters.Length == 0) {
            return queryRoot
                .Sort(SortSelector)
                .Size(searchQuery.Limit);
        }

        _queryFilters.Clear();
        _queries.Clear();
        _keywords.Clear();

        foreach (var filter in searchQuery.Filters ?? Enumerable.Empty<IQueryFilter>()) {
            filter.Apply(this);
        }

        return queryRoot.Query(query => query
            .Bool(boolQuery => boolQuery
                .Filter(_queryFilters.ToArray())
                .Should(_queries.ToArray())))
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

    void IQueryBuilder.ApplyKeywordFilter<TDocument>(KeywordFilter<TDocument> keywordFilter)
        where TDocument : class
    {
        foreach (var keyword in keywordFilter.Keywords ?? Enumerable.Empty<string>()) {
            if (!_keywords.Add(keyword)) {
                continue;
            }
            _queries.Add(new QueryContainerDescriptor<TDocument>()
                .ScriptScore(scoredQuery => scoredQuery
                    .Query(q => q.Match(m => m.Field(f => f.Text).Query(keyword)))
                    .Script(script => script.Source("_score * 1.7")))
            );
        }
    }

    void IQueryBuilder.ApplySemanticFilter<TDocument>(SemanticFilter<TDocument> semanticFilter)
        where TDocument : class
    {
        _queries.Add(new QueryContainerDescriptor<TDocument>()
            .ScriptScore(scoredQuery => scoredQuery
                .Query(q => q.Raw(
                    $$"""
                    {
                        "neural": {
                            "{{EmbeddingFieldName}}": {
                                "query_text": "{{semanticFilter.Text}}",
                                "model_id": "{{settings.ModelId}}",
                                "k": 100
                            }
                        }
                    }
                    """))
                .Script(script => script.Source("_score * 1.5"))));
    }

    void IQueryBuilder.ApplyChatFilter(ChatFilter chatFilter)
    {
        var isPublic = ToBoolTermValue(chatFilter.PublicChatInclusion);
        var isBotChat = ToBoolTermValue(chatFilter.BotChatInclusion);

        if (isPublic.HasValue || isBotChat.HasValue) {
            _queryFilters.Add(new QueryContainerDescriptor<ChatSlice>()
                .HasParent<ChatInfo>(parent => parent
                    .ParentType(ChatInfoToChatSliceRelation.ChatInfoName)
                    .Query(query => query.Bool(parentQuery => parentQuery.Filter([
                            q => isPublic.HasValue ? q.Term(t => t.IsPublic, isPublic.Value) : q,
                            q => isBotChat.HasValue ? q.Term(t => t.IsBotChat, isBotChat.Value) : q,
                        ])))
                ));
        }

        return;

        // NOTE: InclusionMode.Include means no filter required
        static bool? ToBoolTermValue(InclusionMode inclusionMode) => inclusionMode != InclusionMode.Include
            ? inclusionMode == InclusionMode.IncludeStrictly
            : default(bool?);
    }
}
