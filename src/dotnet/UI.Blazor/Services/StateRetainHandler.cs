using Blazored.SessionStorage;

namespace ActualChat.UI.Blazor.Services;

public interface IStateRestoreHandler
{
    int Priority { get; }
    Task Execute();
}

public abstract class StateRestoreHandler<TItemValue> : IStateRestoreHandler
{
    private readonly ISessionStorageService _storage;
    private readonly IStateFactory _stateFactory;
    private IComputedState<TItemValue>? _computed;

    protected StateRestoreHandler(IServiceProvider services)
    {
        _stateFactory = services.GetRequiredService<IStateFactory>();
        _storage = services.GetRequiredService<ISessionStorageService>();
    }

    public int Priority => 10000;

    public async Task Execute()
    {
        var itemValue = await _storage.GetItemAsync<TItemValue>(StoreItemKey).ConfigureAwait(false);
        await Restore(itemValue).ConfigureAwait(false);
        await _storage.RemoveItemAsync(StoreItemKey).ConfigureAwait(false);
        _computed = _stateFactory.NewComputed<TItemValue>(UpdateDelayer, Compute);
        _computed.Updated += ComputedOnUpdated;
    }

    protected abstract string StoreItemKey { get; }

    protected virtual IUpdateDelayer UpdateDelayer => Stl.Fusion.UpdateDelayer.ZeroDelay;

    protected abstract Task Restore(TItemValue? itemValue);

    protected abstract Task<TItemValue> Compute(CancellationToken cancellationToken);

    private Task<TItemValue> Compute(IComputedState<TItemValue> state, CancellationToken cancellationToken)
        => Compute(cancellationToken);

    private void ComputedOnUpdated(IState<TItemValue> arg1, StateEventKind arg2)
        => _storage.SetItemAsync(StoreItemKey, arg1.Value).ConfigureAwait(false);
}
