namespace ActualChat.MLSearch.Indexing;

internal interface ICursorStates<TState> where TState : class
{
    Task<TState?> LoadAsync(string key, CancellationToken cancellationToken);
    Task SaveAsync(string key, TState state, CancellationToken cancellationToken);
}
