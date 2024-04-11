
namespace ActualChat.MLSearch.Engine;

internal sealed class SearchQuery
{
    public IMetadataFilter[]? MetadataFilters;
    public string FreeTextFilter = "";
    public string[]? Keywords;
}
