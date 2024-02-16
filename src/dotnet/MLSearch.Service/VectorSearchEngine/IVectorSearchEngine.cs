
namespace ActualChat.MLSearch;

internal interface IVectorSearchEngine
{
    Task<VectorSearchResult> Find(VectorSearchQuery query, CancellationToken cancellationToken);
}

internal class VectorSearchEngine : IVectorSearchEngine
{
    public Task<VectorSearchResult> Find(VectorSearchQuery query, CancellationToken cancellationToken)
    {
        // Executes search over vector database
        // Returns ranked list of documents as a result
        throw new NotImplementedException();
    }
}
