
namespace ActualChat.MLSearch.SearchEngine.OpenSearch;

internal class OpenSearchEngine : ISearchEngine
{
    public Task<VectorSearchResult> Find(VectorSearchQuery query, CancellationToken cancellationToken)
    {
        // Executes search over vector database
        // Returns ranked list of documents as a result
        throw new NotImplementedException();
    }

    public Task Ingest(IndexedDocument document, CancellationToken cancellationToken) => throw new NotImplementedException();
}
