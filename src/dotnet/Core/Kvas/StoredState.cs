using ActualChat.Pooling;

namespace ActualChat.Kvas;

public interface IStoredState<T> : IMutableState<T>
{
    Task WhenRead { get; }
}

public class StoredState<T> : MutableState<T>, IStoredState<T>
{
    private IStateSnapshot<T>? _snapshotOnSync;
    private ILogger? _log;

    protected ILogger Log => _log ??= Services.LogFor(GetType());
    protected Options Settings { get; }
    protected TaskSource<Unit> WhenReadSource { get; }

    public Task WhenRead => WhenReadSource.Task;

    public StoredState(Options options, IServiceProvider services, bool initialize = true)
        : base(options, services, false)
    {
        Settings = options;
        WhenReadSource = TaskSource.New<Unit>(false);
 #pragma warning disable MA0056
        // ReSharper disable once VirtualMemberCallInConstructor
        if (initialize) Initialize(options);
 #pragma warning restore MA0056
    }

    protected override StateBoundComputed<T> CreateComputed()
    {
        var oldSnapshot = Snapshot;
        var computed = base.CreateComputed();
        if (oldSnapshot == null!) {
            // Initial value
            var firstSnapshot = Snapshot;
            using var _ = ExecutionContextExt.SuppressFlow();
            Task.Run(async () => {
                var valueOpt = Option.None<T>();
                try {
                    valueOpt = await Settings.Read(CancellationToken.None).ConfigureAwait(false);
                }
                catch (Exception e) {
                    Log.LogError(e, "Failed to read the initial value");
                }
                if (valueOpt.IsSome(out var value)) {
                    lock (Lock) {
                        if (Snapshot == firstSnapshot) {
                            _snapshotOnSync = firstSnapshot;
                            Set(value);
                        }
                    }
                }
                WhenReadSource.TrySetResult(default);
            });
        }
        else {
            if (oldSnapshot == _snapshotOnSync)
                _snapshotOnSync = null; // Let's make it available for GC
            else if (computed.IsValue(out var value))
                _ = Settings.Write(value, CancellationToken.None);
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
