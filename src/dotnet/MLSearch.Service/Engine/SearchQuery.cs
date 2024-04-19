
namespace ActualChat.MLSearch.Engine;

internal sealed class SearchQuery
{
    public IMetadataFilter[]? MetadataFilters;
    public string? FreeTextFilter;
    public string[]? Keywords;

    public bool IsEmpty()
        => (MetadataFilters is null || MetadataFilters.Length == 0)
            && FreeTextFilter.IsNullOrEmpty()
            && (Keywords is null || Keywords.Length == 0 || Keywords.All(kw => kw.IsNullOrEmpty()));
}
