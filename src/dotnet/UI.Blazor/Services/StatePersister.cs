using Blazored.SessionStorage;
using Stl.Locking;

namespace ActualChat.UI.Blazor.Services;

public abstract class StatePersisterBase<TState> : IStateRestoreHandler, IDisposable
{
    private readonly Lazy<string> _storageKeyLazy;

    protected ILogger Log { get; }
    protected IServiceProvider Services { get; }
    protected IStateFactory StateFactory { get; }
    protected IAsyncLock Lock { get; } = new AsyncLock(ReentryMode.CheckedFail);
    protected IComputedState<TState>? Computed { get; set; }
    protected string StorageKey => _storageKeyLazy.Value;
    protected string? LastSavedText { get; set; }

    public string Key { get; init; }
    public double Priority { get; init; }
    public ITextSerializer<TState> Serializer { get; init; } = TextSerializer.Default.ToTyped<TState>();

    protected StatePersisterBase(IServiceProvider services)
    {
        Services = services;
        Log = services.LogFor(GetType());
        Key = GetType().Name.TrimSuffix("StatePersister");
#pragma warning disable MA0056
        _storageKeyLazy = new Lazy<string>(GetStorageKey);
#pragma warning restore MA0056
        StateFactory = services.StateFactory();
    }

    public void Dispose()
        => Computed?.Dispose();

    public async Task Restore(CancellationToken cancellationToken)
    {
        using var _1 = await Lock.Lock(cancellationToken).ConfigureAwait(false);

        if (Computed != null)
            return; // Already restored
        try {
            var stateOpt = await Load(cancellationToken).ConfigureAwait(false);
            if (stateOpt.IsSome(out var state))
                await Restore(state, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException) {
            Log.LogError(ex, "State restore failed, Key = {Key}", Key);
        }

        Computed = StateFactory.NewComputed(GetStateOptions(), Compute);
        Computed.Updated += OnComputedUpdated;
    }

    protected abstract Task Restore(TState? state, CancellationToken cancellationToken);
    protected abstract Task<TState> Compute(CancellationToken cancellationToken);
    protected abstract ValueTask SaveText(string text, CancellationToken cancellationToken);
    protected abstract ValueTask<string> LoadText(CancellationToken cancellationToken);

    protected virtual async Task Save(TState state, CancellationToken cancellationToken)
    {
        using var _ = await Lock.Lock(cancellationToken).ConfigureAwait(false);
        var text = Serializer.Write(state);
        if (OrdinalEquals(LastSavedText, text))
            return;
        await SaveText(text, cancellationToken).ConfigureAwait(false);
        LastSavedText = text;
    }

    protected virtual async Task<Option<TState>> Load(CancellationToken cancellationToken)
    {
        var text = LastSavedText ??= await LoadText(cancellationToken).ConfigureAwait(false);
        return text.IsNullOrEmpty() ? Option<TState>.None : Serializer.Read(text);
    }

    protected virtual ComputedState<TState>.Options GetStateOptions()
        => new() { UpdateDelayer = new UpdateDelayer(UIActionTracker.None, 1) }; // 1 second update delay

    protected virtual string GetStorageKey()
        => $"State.{Key}";

    // Private methods

    private Task<TState> Compute(IComputedState<TState> state, CancellationToken cancellationToken)
        => Compute(cancellationToken);

    private void OnComputedUpdated(IState<TState> state, StateEventKind eventKind)
        => _ = Save(state.Value, CancellationToken.None);
}

public abstract class StatePersister<TState> : StatePersisterBase<TState>
{
    protected ISessionStorageService Storage { get; }

    protected StatePersister(IServiceProvider services) : base(services)
        => Storage = services.GetRequiredService<ISessionStorageService>();

    protected override async ValueTask<string> LoadText(CancellationToken cancellationToken)
    {
        var text = await Storage.GetItemAsStringAsync(StorageKey, cancellationToken).ConfigureAwait(false);
        return text ?? "";
    }

    protected override async ValueTask SaveText(string text, CancellationToken cancellationToken)
    {
        if (text.IsNullOrEmpty())
            await Storage.RemoveItemAsync(StorageKey, cancellationToken).ConfigureAwait(false);
        else
            await Storage.SetItemAsStringAsync(StorageKey, text, cancellationToken).ConfigureAwait(false);
    }
}
