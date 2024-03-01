
namespace ActualChat.MLSearch.Engine;

internal interface ISearchEngine
{
    Task<VectorSearchResult<TDocument>> Find<TDocument>(VectorSearchQuery query, CancellationToken cancellationToken)
        where TDocument : class;

    Task Ingest<TDocument>(TDocument document, CancellationToken cancellationToken)
        where TDocument : class;
}
