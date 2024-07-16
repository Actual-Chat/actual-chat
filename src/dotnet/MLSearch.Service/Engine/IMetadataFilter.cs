
using ActualChat.MLSearch.Documents;

namespace ActualChat.MLSearch.Engine;

internal interface IQueryFilter
{
    void Apply(IQueryBuilder queryBuilder);
}

internal sealed class FreeTextFilter<TDocument>(string text) : IQueryFilter
    where TDocument : class
{
    public string Text { get; } = text;
    public void Apply(IQueryBuilder queryBuilder) => queryBuilder.ApplyFreeTextFilter(this);
}

internal sealed class KeywordFilter<TDocument>(IReadOnlyCollection<string> keywords) : IQueryFilter
    where TDocument : class, IHasText
{
    public IReadOnlyCollection<string> Keywords { get; } = keywords;
    public void Apply(IQueryBuilder queryBuilder) => queryBuilder.ApplyKeywordFilter(this);
}

internal sealed class OrFilter(IEnumerable<IQueryFilter> filters) : IQueryFilter
{
    public IEnumerable<IQueryFilter> Filters { get; } = filters;

    public void Apply(IQueryBuilder queryBuilder) => queryBuilder.ApplyOrFilter(this);
}

internal sealed class EqualityFilter<TValue>(string fieldName, TValue value) : IQueryFilter
{
    public string FieldName { get; } = fieldName;
    public TValue Value { get; } = value;

    public void Apply(IQueryBuilder queryBuilder) => queryBuilder.ApplyEqualityFilter(this);
}

internal abstract class RangeFilter<TValue>(string fieldName, RangeBound<TValue>? from, RangeBound<TValue>? to)
    : IQueryFilter
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
