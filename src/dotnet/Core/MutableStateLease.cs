using System.Diagnostics.CodeAnalysis;
using ActualChat.Pooling;
using Stl.Conversion;

namespace ActualChat;

public interface IMutableStateLease<T> : IMutableState<T>, IDisposable
{ }

public class MutableStateLease<T, TKey, TState, TResource>(
    SharedResourcePool<TKey, TResource>.Lease lease,
    TState state
    ) : IMutableStateLease<T>
    where TState : class, IMutableState<T>
    where TResource : class
    where TKey : notnull
{
    protected SharedResourcePool<TKey, TResource>.Lease Lease { get; } = lease;

    public TState State { get; } = state;
    public IServiceProvider Services => State.Services;

    public T Value {
        get => State.Value;
        set => State.Value = value;
    }

    public object? UntypedValue {
        get => State.UntypedValue;
        set => State.UntypedValue = value;
    }

    public T? ValueOrDefault => State.ValueOrDefault;

    public Exception? Error {
        get => State.Error;
        set => State.Error = value;
    }

    public bool HasValue => State.HasValue;
    public bool HasError => State.HasError;

    object? IState.LastNonErrorValue => ((IState)State).LastNonErrorValue; // Intended use of LatestNonErrorValue
    public T LastNonErrorValue => State.LastNonErrorValue; // Intended use of LatestNonErrorValue

    IStateSnapshot IState.Snapshot => ((IState)State).Snapshot;
    public StateSnapshot<T> Snapshot => State.Snapshot;

    IComputed IState.Computed => ((IState)State).Computed;
    public Computed<T> Computed => State.Computed;

    public virtual void Dispose()
        => Lease.Dispose();

    public void Deconstruct(out T value, out Exception? error)
        => State.Deconstruct(out value, out error);

    public void Set(Result<T> result)
        => State.Set(result);

    public void Set(Func<Result<T>, Result<T>> updater)
        => State.Set(updater);

    public void Set<TOtherState>(TOtherState state, Func<TOtherState, Result<T>, Result<T>> updater)
        => State.Set(state, updater);

    public void Set(IResult result)
        => State.Set(result);

    public bool IsValue([MaybeNullWhen(false)] out T value)
        => State.IsValue(out value);

    public bool IsValue([MaybeNullWhen(false)] out T value, [MaybeNullWhen(true)] out Exception error)
        => State.IsValue(out value, out error);

    public Result<T> AsResult()
        => State.AsResult();

    public Result<TOther> Cast<TOther>()
        => State.Cast<TOther>();

    T IConvertibleTo<T>.Convert()
        => ((IConvertibleTo<T>)State).Convert();

    Result<T> IConvertibleTo<Result<T>>.Convert()
        => ((IConvertibleTo<Result<T>>)State).Convert();

    // Events

    event Action<IState<T>, StateEventKind>? IState<T>.Invalidated {
        add => State.Invalidated += value;
        remove => State.Invalidated -= value;
    }

    event Action<IState<T>, StateEventKind>? IState<T>.Updating {
        add => State.Updating += value;
        remove => State.Updating -= value;
    }

    event Action<IState<T>, StateEventKind>? IState<T>.Updated {
        add => State.Updated += value;
        remove => State.Updated -= value;
    }

    event Action<IState, StateEventKind>? IState.Invalidated {
        add => ((IState)State).Invalidated += value;
        remove => ((IState)State).Invalidated -= value;
    }

    event Action<IState, StateEventKind>? IState.Updating {
        add => ((IState)State).Updating += value;
        remove => ((IState)State).Updating -= value;
    }

    event Action<IState, StateEventKind>? IState.Updated {
        add => ((IState)State).Updated += value;
        remove => ((IState)State).Updated -= value;
    }
}

public class MutableStateLease<T, TState>(SharedResourcePool<Symbol, TState>.Lease lease)
    : MutableStateLease<T, Symbol, TState, TState>(lease, lease.Resource)
    where TState : class, IMutableState<T>;

public class MutableStateLease<T>(SharedResourcePool<Symbol, IMutableState<T>>.Lease lease)
    : MutableStateLease<T, IMutableState<T>>(lease);
