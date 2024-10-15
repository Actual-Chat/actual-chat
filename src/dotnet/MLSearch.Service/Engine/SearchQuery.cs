
namespace ActualChat.MLSearch.Engine;

public sealed class SearchQuery
{
    public IQueryFilter[]? Filters;
    public SortStatement[]? SortStatements;
    public int? Limit;
}

public enum QuerySortOrder
{
    Ascending = 0,
    Descending = 1,
}

public enum MultiValueFieldMode
{
    Min = 0,
    Max = 1,
}

public record SortStatement(string Field, QuerySortOrder SortOrder, MultiValueFieldMode Mode);
