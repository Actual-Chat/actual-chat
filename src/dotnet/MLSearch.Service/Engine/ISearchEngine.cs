
namespace ActualChat.MLSearch.Engine;

internal interface ISearchEngine
{
    Task<SearchResult<TDocument>> Find<TDocument>(SearchQuery query, CancellationToken cancellationToken)
        where TDocument : class;

    Task Ingest<TDocument>(TDocument document, CancellationToken cancellationToken)
        where TDocument : class;
}
