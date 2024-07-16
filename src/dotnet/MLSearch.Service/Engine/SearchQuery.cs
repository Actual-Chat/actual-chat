
namespace ActualChat.MLSearch.Engine;

internal sealed class SearchQuery
{
    public IQueryFilter[]? Filters;
    public SortStatement[]? SortStatements;
    public int? Limit;
}

internal enum QuerySortOrder
{
    Ascending = 1,
    Descenging = 2,
}
internal enum MultivalueFieldMode
{
    Min = 1,
    Max = 2,
}
internal record SortStatement(string Field, QuerySortOrder SortOrder, MultivalueFieldMode Mode);
