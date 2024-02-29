using ActualChat.MLSearch.SearchEngine.OpenSearch;

namespace ActualChat.MLSearch;

internal interface IMetadataFilter
{
    void Apply(IQueryBuilder queryBuilder);
}

internal class EqualityFilter<TValue>(string fieldName, TValue value) : IMetadataFilter
{
    public string FieldName { get; } = fieldName;
    public TValue Value { get; } = value;

    public void Apply(IQueryBuilder queryBuilder) => queryBuilder.ApplyEqualityFilter(this);
}

internal abstract class RangeFilter<TValue>(string fieldName, RangeBound<TValue>? from, RangeBound<TValue>? to) : IMetadataFilter
    where TValue: struct
{
    public string FieldName { get; } = fieldName;
    public RangeBound<TValue>? From { get; } = from;
    public RangeBound<TValue>? To { get; } = to;

    public abstract void Apply(IQueryBuilder queryBuilder);
}

internal sealed class DoubleRangeFilter(string fieldName, RangeBound<double>? from, RangeBound<double>? to)
    : RangeFilter<double>(fieldName, from, to)
{
    public override void Apply(IQueryBuilder queryBuilder) => queryBuilder.ApplyRangeFilter(this);
}

internal sealed class Int32RangeFilter(string fieldName, RangeBound<int>? from, RangeBound<int>? to)
    : RangeFilter<int>(fieldName, from, to)
{
    public override void Apply(IQueryBuilder queryBuilder) => queryBuilder.ApplyRangeFilter(this);
}

internal sealed class Int64RangeFilter(string fieldName, RangeBound<long>? from, RangeBound<long>? to)
    : RangeFilter<long>(fieldName, from, to)
{
    public override void Apply(IQueryBuilder queryBuilder) => queryBuilder.ApplyRangeFilter(this);
}

internal sealed class DateRangeFilter(string fieldName, RangeBound<DateTime>? from, RangeBound<DateTime>? to)
    : RangeFilter<DateTime>(fieldName, from, to)
{
    public override void Apply(IQueryBuilder queryBuilder) => queryBuilder.ApplyRangeFilter(this);
}

[StructLayout(LayoutKind.Auto)]
internal record struct RangeBound<TValue>(TValue Value, bool Include);
