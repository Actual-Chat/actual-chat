using System.Diagnostics.CodeAnalysis;
using ActualChat.Pooling;
using Stl.Conversion;

namespace ActualChat.UI.Blazor.Services;

public sealed class PersistentStateLease<T>: IPersistentState<T>
{
    private readonly SharedResourcePool<Symbol, IPersistentState<T>>.Lease _lease;
    private readonly IPersistentState<T> _source;

    public PersistentStateLease(SharedResourcePool<Symbol, IPersistentState<T>>.Lease lease)
    {
        _lease = lease;
        _source = lease.Resource;
    }

    public ValueTask DisposeAsync()
    {
        _lease.DisposeSilently();
        return ValueTask.CompletedTask;
    }

    public Result<TOther> Cast<TOther>()
        => _source.Cast<TOther>();

    public bool HasValue => _source.HasValue;

    public void Set(Result<T> result)
        => _source.Set(result);
    public void Set(Func<Result<T>, Result<T>> updater)
        => _source.Set(updater);
    public void Set<TState>(TState state, Func<TState, Result<T>, Result<T>> updater)
        => _source.Set(state, updater);

    T IMutableResult<T>.Value {
        get => _source.Value;
        set => _source.Value = value;
    }

    T IResult<T>.Value => _source.Value;

    public void Deconstruct(out T value, out Exception? error)
        => _source.Deconstruct(out value, out error);

    public bool IsValue([MaybeNullWhen(false)] out T value)
        => _source.IsValue(out value);

    public bool IsValue([MaybeNullWhen(false)]out T value, [MaybeNullWhen(true)]out Exception error)
        => _source.IsValue(out value, out error);

    public Result<T> AsResult()
        => _source.AsResult();

    public T? ValueOrDefault => _source.ValueOrDefault;

    object? IResult.UntypedValue => ((IResult)_source).UntypedValue;

    public bool HasError => _source.HasError;

    Exception? IMutableResult.Error {
        get => _source.Error;
        set => _source.Error = value;
    }

    public void Set(IResult result)
        => _source.Set(result);

    public object? UntypedValue {
        get => _source.UntypedValue;
        set => _source.UntypedValue = value;
    }

    Exception? IResult.Error => ((IResult)_source).Error;

    public IServiceProvider Services => _source.Services;

    IStateSnapshot IState.Snapshot => ((IState)_source).Snapshot;

    public IComputed<T> Computed => _source.Computed;

    public T LatestNonErrorValue => _source.LatestNonErrorValue;

    event Action<IState<T>, StateEventKind>? IState<T>.Invalidated {
        add => _source.Invalidated += value;
        remove => _source.Invalidated -= value;
    }

    event Action<IState<T>, StateEventKind>? IState<T>.Updating {
        add => _source.Updating += value;
        remove => _source.Updating -= value;
    }

    event Action<IState<T>, StateEventKind>? IState<T>.Updated {
        add => _source.Updated += value;
        remove => _source.Updated -= value;
    }

    public StateSnapshot<T> Snapshot => _source.Snapshot;

    IComputed IState.Computed => ((IState)_source).Computed;

    object? IState.LatestNonErrorValue => ((IState)_source).LatestNonErrorValue;

    event Action<IState, StateEventKind>? IState.Invalidated {
        add => ((IState)_source).Invalidated += value;
        remove => ((IState)_source).Invalidated -= value;
    }

    event Action<IState, StateEventKind>? IState.Updating {
        add => ((IState)_source).Updating += value;
        remove => ((IState)_source).Updating -= value;
    }

    event Action<IState, StateEventKind>? IState.Updated {
        add => ((IState)_source).Updated += value;
        remove => ((IState)_source).Updated -= value;
    }

    T IConvertibleTo<T>.Convert()
        => ((IConvertibleTo<T>)_source).Convert();

    Result<T> IConvertibleTo<Result<T>>.Convert()
        => ((IConvertibleTo<Result<T>>)_source).Convert();

}
