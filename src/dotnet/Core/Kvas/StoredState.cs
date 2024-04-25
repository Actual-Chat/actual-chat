using System.Diagnostics.CodeAnalysis;
using ActualChat.Pooling;

namespace ActualChat.Kvas;

public interface IStoredState<
    [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] T>
    : IMutableState<T>
{
    Task WhenRead { get; }
}

public sealed class StoredState<
    [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] T>
    : MutableState<T>, IStoredState<T>
{
    private readonly TaskCompletionSource _whenReadSource = TaskCompletionSourceExt.New();

    private Options Settings { get; }
    private ILogger? DebugLog => Constants.DebugMode.StoredState ? Log : null;

    public Task WhenRead => _whenReadSource.Task;

    public StoredState(Options options, IServiceProvider services, bool initialize = true)
        : base(options, services, false)
    {
        Settings = options;
        // ReSharper disable once VirtualMemberCallInConstructor
        if (initialize)
            Initialize(options);
    }

    protected override StateBoundComputed<T> CreateComputed()
    {
        var computed = base.CreateComputed();
        var snapshot = Snapshot;
        if (snapshot.IsInitial) {
            // Initial value
            var initialSnapshot = snapshot;
            _ = ForegroundTask.Run(async () => {
                var valueOpt = Option.None<T>();
                try {
                    using var _ = ComputeContext.BeginIsolation();
                    try {
                        valueOpt = await Settings.Read(CancellationToken.None).ConfigureAwait(false);
                    }
                    catch (Exception e) {
                        Log.LogError(e, "Read failed");
                    }
                    if (valueOpt.IsSome(out var value)) {
                        bool mustSet;
                        lock (Lock) {
                            mustSet = Snapshot == initialSnapshot;
                            if (mustSet) {
                                Set(value);
                                computed = (StateBoundComputed<T>)Computed;
                            }
                        }
                        if (mustSet)
                            DebugLog?.LogDebug("{State}: Read = {Result}", this, value);
                        else
                            DebugLog?.LogDebug("{State}: Read: skipping (already changed)", this);
                    }
                    else
                        DebugLog?.LogDebug("{State}: Read: skipping (no value stored or read error)", this);
                }
                finally {
                    _whenReadSource.TrySetResult();
                }
            });
        }
        else {
            // Subsequent change
            if (computed.IsValue(out var value)) {
                using var _1 = ComputeContext.BeginIsolation();
                _ = Settings.Write(value, CancellationToken.None);
                DebugLog?.LogDebug("{State}: Write = {Result}", this, value);
            }
        }
        return computed;
    }

    // Nested types

    public new abstract record Options : MutableState<T>.Options
    {
        internal abstract ValueTask<Option<T>> Read(CancellationToken cancellationToken);
        internal abstract Task Write(T value, CancellationToken cancellationToken);
    }

    public record CustomOptions(
        Func<CancellationToken, ValueTask<Option<T>>> Reader,
        Func<T, CancellationToken, Task> Writer
        ) : Options
    {
        internal override ValueTask<Option<T>> Read(CancellationToken cancellationToken)
            => Reader.Invoke(cancellationToken);
        internal override Task Write(T value, CancellationToken cancellationToken)
            => Writer.Invoke(value, cancellationToken);
    }

    public record KvasOptions(IKvas Kvas, string Key) : Options
    {
        public Func<T, CancellationToken, ValueTask<T>>? Corrector { get; init; }

        internal override async ValueTask<Option<T>> Read(CancellationToken cancellationToken)
        {
            var valueOpt = await Kvas.TryGet<T>(Key, cancellationToken).ConfigureAwait(false);
            if (!valueOpt.IsSome(out var value))
                return default;

            if (Corrector != null)
                value = await Corrector.Invoke(value, cancellationToken).ConfigureAwait(false);
            return value;
        }

        internal override Task Write(T value, CancellationToken cancellationToken)
            => Kvas.Set(Key, value, cancellationToken);
    }
}

public class StoredStateLease<
    [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] T>(SharedResourcePool<Symbol, IStoredState<T>>.Lease lease)
    : MutableStateLease<T, IStoredState<T>>(lease), IStoredState<T>
{
    public Task WhenRead => State.WhenRead;
}
