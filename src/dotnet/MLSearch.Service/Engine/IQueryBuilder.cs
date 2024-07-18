using ActualChat.MLSearch.Documents;

namespace ActualChat.MLSearch.Engine;

public interface IQueryBuilder
{
    void ApplyOrFilter(OrFilter orFilter);
    void ApplyEqualityFilter<TValue>(EqualityFilter<TValue> equalityFilter);
    void ApplyRangeFilter(DoubleRangeFilter rangeFilter);
    void ApplyRangeFilter(Int32RangeFilter rangeFilter);
    void ApplyRangeFilter(Int64RangeFilter rangeFilter);
    void ApplyRangeFilter(DateRangeFilter rangeFilter);
    void ApplyKeywordFilter<TDocument>(KeywordFilter<TDocument> keywordFilter)
        where TDocument : class, IHasText;
    void ApplySemanticFilter<TDocument>(SemanticFilter<TDocument> semanticFilter)
        where TDocument : class;
    void ApplyChatFilter(ChatFilter chatFilter);
}
