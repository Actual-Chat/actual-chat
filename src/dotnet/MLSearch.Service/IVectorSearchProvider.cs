
namespace ActualChat.MLSearch;

internal interface IVectorSearchProvider
{
    Task<VectorSearchResult> Find(VectorSearchQuery query, CancellationToken cancellationToken);
}

internal class VectorSearchQuery
{
    public MetadataFilter MetadataFilter;
    public string FreeTextFilter;
}

public class MetadataFilter
{
}

internal class VectorSearchResult
{
    public VectorSearchRankedDocument[] Documents;
}

internal class VectorSearchRankedDocument
{
    public double Rank;
    public IndexedDocument Document;
}

internal class IndexedDocument
{
    public string Uri;
    public string Text;
}
