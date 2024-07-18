
namespace ActualChat.MLSearch.Engine;

public interface ISearchEngine<TDocument>
    where TDocument : class
{
    Task<SearchResult<TDocument>> Find(SearchQuery query, CancellationToken cancellationToken);
}
