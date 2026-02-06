namespace ActualChat.Search;

/// <summary>
/// Interface for search providers that return typed search results.
/// </summary>
public interface ISearchProvider<TResult>
    where TResult : SearchResult
{
    Task<TResult[]> Find(string filter, int limit, CancellationToken cancellationToken);
}
