namespace ActualChat.Search;

public interface ISearchProvider<TResult>
    where TResult : SearchResult
{
    Task<TResult[]> Find(string filter, int limit, CancellationToken cancellationToken);
}
