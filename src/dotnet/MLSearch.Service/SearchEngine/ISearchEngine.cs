
namespace ActualChat.MLSearch;

internal interface ISearchEngine
{
    Task<VectorSearchResult> Find(VectorSearchQuery query, CancellationToken cancellationToken);

    Task Ingest(ChatSlice document, CancellationToken cancellationToken);
}
