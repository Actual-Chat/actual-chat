
using ActualChat.MLSearch.Documents;

namespace ActualChat.MLSearch.Engine;

public interface IQueryFilter
{
    void Apply(IQueryBuilder queryBuilder);
}

public sealed class SemanticFilter<TDocument>(string text) : IQueryFilter
    where TDocument : class
{
    public string Text { get; } = text;
    public void Apply(IQueryBuilder queryBuilder) => queryBuilder.ApplySemanticFilter(this);
}

public sealed class KeywordFilter<TDocument>(IReadOnlyCollection<string> keywords) : IQueryFilter
    where TDocument : class, IHasText
{
    public IReadOnlyCollection<string> Keywords { get; } = keywords;
    public void Apply(IQueryBuilder queryBuilder) => queryBuilder.ApplyKeywordFilter(this);
}

public sealed class OrFilter(IEnumerable<IQueryFilter> filters) : IQueryFilter
{
    public IEnumerable<IQueryFilter> Filters { get; } = filters;

    public void Apply(IQueryBuilder queryBuilder) => queryBuilder.ApplyOrFilter(this);
}

public sealed class EqualityFilter<TValue>(string fieldName, TValue value) : IQueryFilter
{
    public string FieldName { get; } = fieldName;
    public TValue Value { get; } = value;

    public void Apply(IQueryBuilder queryBuilder) => queryBuilder.ApplyEqualityFilter(this);
}

public abstract class RangeFilter<TValue>(string fieldName, RangeBound<TValue>? from, RangeBound<TValue>? to)
    : IQueryFilter
    where TValue: struct
{
    public string FieldName { get; } = fieldName;
    public RangeBound<TValue>? From { get; } = from;
    public RangeBound<TValue>? To { get; } = to;

    public abstract void Apply(IQueryBuilder queryBuilder);
}

public sealed class DoubleRangeFilter(string fieldName, RangeBound<double>? from, RangeBound<double>? to)
    : RangeFilter<double>(fieldName, from, to)
{
    public override void Apply(IQueryBuilder queryBuilder) => queryBuilder.ApplyRangeFilter(this);
}

public sealed class Int32RangeFilter(string fieldName, RangeBound<int>? from, RangeBound<int>? to)
    : RangeFilter<int>(fieldName, from, to)
{
    public override void Apply(IQueryBuilder queryBuilder) => queryBuilder.ApplyRangeFilter(this);
}

public sealed class Int64RangeFilter(string fieldName, RangeBound<long>? from, RangeBound<long>? to)
    : RangeFilter<long>(fieldName, from, to)
{
    public override void Apply(IQueryBuilder queryBuilder) => queryBuilder.ApplyRangeFilter(this);
}

public sealed class DateRangeFilter(string fieldName, RangeBound<DateTime>? from, RangeBound<DateTime>? to)
    : RangeFilter<DateTime>(fieldName, from, to)
{
    public override void Apply(IQueryBuilder queryBuilder) => queryBuilder.ApplyRangeFilter(this);
}

[StructLayout(LayoutKind.Auto)]
public record struct RangeBound<TValue>(TValue Value, bool Include);
