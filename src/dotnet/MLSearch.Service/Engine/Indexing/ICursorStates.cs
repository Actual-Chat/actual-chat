namespace ActualChat.MLSearch.Engine.Indexing;

internal interface ICursorStates<TState> where TState : class
{
    Task<TState?> Load(string key, CancellationToken cancellationToken);
    Task Save(string key, TState state, CancellationToken cancellationToken);
}
