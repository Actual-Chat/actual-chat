
namespace ActualChat.MLSearch.Engine;

internal class SearchQuery
{
    public IMetadataFilter[]? MetadataFilters;
    public string FreeTextFilter;
    public string[]? Keywords;
}
