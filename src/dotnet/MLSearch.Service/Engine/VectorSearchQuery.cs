
namespace ActualChat.MLSearch.Engine;

internal class VectorSearchQuery
{
    public IMetadataFilter[]? MetadataFilters;
    public string FreeTextFilter;
    public string[]? Keywords;
}
