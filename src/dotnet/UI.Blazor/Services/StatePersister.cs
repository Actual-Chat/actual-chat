using Blazored.SessionStorage;

namespace ActualChat.UI.Blazor.Services;

public abstract class StatePersister<TState> : IStateRestoreHandler, IDisposable
{
    private readonly ISessionStorageService _storage;
    private readonly IStateFactory _stateFactory;
    private Lazy<string> _storageKeyLazy;
    private IComputedState<TState>? _computed;

    protected string StorageKey => _storageKeyLazy.Value;

    public string Key { get; init; }
    public double Priority { get; init; }

    protected StatePersister(IServiceProvider services)
    {
        Key = GetType().Name.TrimSuffix("StatePersister");
#pragma warning disable MA0056
        _storageKeyLazy = new Lazy<string>(GetStorageKey);
#pragma warning restore MA0056
        _stateFactory = services.StateFactory();
        _storage = services.GetRequiredService<ISessionStorageService>();
    }

    public void Dispose()
        => _computed?.Dispose();

    public async Task Restore()
    {
        TState? state = default;
        bool fetched = false;
        try {
            state = await _storage.GetItemAsync<TState?>(StorageKey).ConfigureAwait(false);
            fetched = true;
        }
        catch {
            // Intended
        }
        if (fetched)
            await Restore(state).ConfigureAwait(false);

        await _storage.RemoveItemAsync(StorageKey).ConfigureAwait(false);
        _computed = _stateFactory.NewComputed(GetStateOptions(), Compute);
        _computed.Updated += OnUpdated;
    }

    protected abstract Task Restore(TState? state);

    protected virtual ComputedState<TState>.Options GetStateOptions()
        => new();

    protected abstract Task<TState> Compute(CancellationToken cancellationToken);

    protected virtual string GetStorageKey()
        => $"State.{Key}";

    // Private methods

    private Task<TState> Compute(IComputedState<TState> state, CancellationToken cancellationToken)
        => Compute(cancellationToken);

    private void OnUpdated(IState<TState> state, StateEventKind eventKind)
        => _storage.SetItemAsync(StorageKey, state.Value).ConfigureAwait(false);
}
