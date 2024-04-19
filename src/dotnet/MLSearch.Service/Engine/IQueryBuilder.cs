namespace ActualChat.MLSearch.Engine;

internal interface IQueryBuilder
{
    void ApplyOrFilter(OrFilter orFilter);
    void ApplyEqualityFilter<TValue>(EqualityFilter<TValue> equalityFilter);
    void ApplyRangeFilter(DoubleRangeFilter rangeFilter);
    void ApplyRangeFilter(Int32RangeFilter rangeFilter);
    void ApplyRangeFilter(Int64RangeFilter rangeFilter);
    void ApplyRangeFilter(DateRangeFilter rangeFilter);
}
