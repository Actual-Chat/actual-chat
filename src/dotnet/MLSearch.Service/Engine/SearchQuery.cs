
namespace ActualChat.MLSearch.Engine;

public sealed class SearchQuery
{
    public IQueryFilter[]? Filters;
    public SortStatement[]? SortStatements;
    public int? Limit;
}

public enum QuerySortOrder
{
    Ascending = 1,
    Descenging = 2,
}
public enum MultivalueFieldMode
{
    Min = 1,
    Max = 2,
}
public record SortStatement(string Field, QuerySortOrder SortOrder, MultivalueFieldMode Mode);
