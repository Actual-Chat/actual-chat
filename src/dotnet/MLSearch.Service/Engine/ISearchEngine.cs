
namespace ActualChat.MLSearch.Engine;

internal interface ISearchEngine<TDocument>
    where TDocument : class
{
    Task<SearchResult<TDocument>> Find(SearchQuery query, CancellationToken cancellationToken);
}
