
namespace ActualChat.MLSearch.Engine;

internal sealed class SearchQuery
{
    public IMetadataFilter[]? MetadataFilters;
    public string? FreeTextFilter;
    public string[]? Keywords;

    public SortStatement[]? SortStatements;
    public int? Limit;

    public bool IsUnfiltered()
        => (MetadataFilters is null || MetadataFilters.Length == 0)
            && FreeTextFilter.IsNullOrEmpty()
            && (Keywords is null || Keywords.Length == 0 || Keywords.All(kw => kw.IsNullOrEmpty()));
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
