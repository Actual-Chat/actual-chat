using System.Diagnostics.CodeAnalysis;
using ActualChat.Pooling;
using ActualLab.Conversion;

namespace ActualChat;

public interface IComputedStateLease<T> : IComputedState<T>;

public class ComputedStateLease<T, TKey, TState, TResource>(
    SharedResourcePool<TKey, TResource>.Lease lease,
    Func<TResource, TState> stateGetter
    ) : IComputedStateLease<T>
    where TState : class, IComputedState<T>
    where TResource : class
    where TKey : notnull
{
    protected SharedResourcePool<TKey, TResource>.Lease Lease { get; } = lease;

    public TState State { get; } = stateGetter.Invoke(lease.Resource);
    public IServiceProvider Services => State.Services;

    public T Value => State.Value;

    public object? UntypedValue => ((IComputedState)State).UntypedValue;

    public T? ValueOrDefault => State.ValueOrDefault;

    public Exception? Error => State.Error;

    public bool HasValue => State.HasValue;
    public bool HasError => State.HasError;

    object? IState.LastNonErrorValue => ((IState)State).LastNonErrorValue; // Intended use of LatestNonErrorValue
    public T LastNonErrorValue => State.LastNonErrorValue; // Intended use of LatestNonErrorValue

    IStateSnapshot IState.Snapshot => ((IState)State).Snapshot;
    public StateSnapshot<T> Snapshot => State.Snapshot;

    IComputed IState.Computed => ((IState)State).Computed;
    public Computed<T> Computed => State.Computed;


    public bool IsDisposed => State.IsDisposed;
    public Task? WhenDisposed => State.WhenDisposed;

    public IUpdateDelayer UpdateDelayer {
        get => State.UpdateDelayer;
        set => State.UpdateDelayer = value;
    }

    public Task UpdateCycleTask => State.UpdateCycleTask;
    public CancellationToken DisposeToken => State.DisposeToken;
    public CancellationToken GracefulDisposeToken => State.GracefulDisposeToken;

    public virtual void Dispose()
        => Lease.Dispose();

    public void Deconstruct(out T value, out Exception? error)
        => State.Deconstruct(out value, out error);

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

public class ComputedStateLease<T, TState>(SharedResourcePool<Symbol, TState>.Lease lease)
    : ComputedStateLease<T, Symbol, TState, TState>(lease, state => state)
    where TState : class, IComputedState<T>
{
    // ReSharper disable once MemberCanBeProtected.Global
}

public class ComputedStateLease<T>(SharedResourcePool<Symbol, ComputedState<T>>.Lease lease)
    : ComputedStateLease<T, ComputedState<T>>(lease)
{
    // ReSharper disable once MemberCanBeProtected.Global
}
