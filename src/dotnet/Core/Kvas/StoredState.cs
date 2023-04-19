using ActualChat.Pooling;

namespace ActualChat.Kvas;

public interface IStoredState<T> : IMutableState<T>
{
    Task WhenRead { get; }
}

public sealed class StoredState<T> : MutableState<T>, IStoredState<T>
{
    private readonly TaskCompletionSource<Unit> _whenReadSource = TaskCompletionSourceExt.New<Unit>();

    private Options Settings { get; }
    private ILogger? DebugLog => Constants.DebugMode.StoredState ? Log : null;

    public Task WhenRead => _whenReadSource.Task;

    public StoredState(Options options, IServiceProvider services, bool initialize = true)
        : base(options, services, false)
    {
        Settings = options;
 #pragma warning disable MA0056
        // ReSharper disable once VirtualMemberCallInConstructor
        if (initialize) Initialize(options);
 #pragma warning restore MA0056
    }

    protected override StateBoundComputed<T> CreateComputed()
    {
        var computed = base.CreateComputed();
        var snapshot = Snapshot;
        if (snapshot.UpdateCount == 0) {
            // Initial value
            var initialSnapshot = snapshot;
            ForegroundTask.Run(async () => {
                var valueOpt = Option.None<T>();
                try {
                    using var _ = Stl.Fusion.Computed.SuspendDependencyCapture();
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
                            if (mustSet)
                                Set(value);
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
                    _whenReadSource.TrySetResult(default);
                }
            });
        }
        else {
            // Subsequent change
            if (computed.IsValue(out var value)) {
                using var _1 = Stl.Fusion.Computed.SuspendDependencyCapture();
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
        public static ITextSerializer<T> DefaultSerializer { get; set; } =
            SystemJsonSerializer.Default.ToTyped<T>();

        public Func<T, CancellationToken, ValueTask<T>>? Corrector { get; init; }
        public ITextSerializer<T> Serializer { get; init; } = DefaultSerializer;

        internal override async ValueTask<Option<T>> Read(CancellationToken cancellationToken)
        {
            var data = await Kvas.Get(Key, cancellationToken).ConfigureAwait(false);
            if (data == null)
                return default;
            var value = Serializer.Read(data);
            if (Corrector != null)
                value = await Corrector.Invoke(value, cancellationToken).ConfigureAwait(false);
            return value;
        }

        internal override Task Write(T value, CancellationToken cancellationToken)
        {
            var data = Serializer.Write(value);
            return Kvas.Set(Key, data, cancellationToken);
        }
    }
}

public class StoredStateLease<T> : MutableStateLease<T, IStoredState<T>>, IStoredState<T>
{
    public Task WhenRead => State.WhenRead;

    public StoredStateLease(SharedResourcePool<Symbol, IStoredState<T>>.Lease lease) : base(lease) { }
}
