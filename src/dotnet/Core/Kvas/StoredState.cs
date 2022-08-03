namespace ActualChat.Kvas;

public interface IStoredState<T> : IMutableState<T>
{ }

public class StoredState<T> : MutableState<T>, IStoredState<T>
{
    private IStateSnapshot<T>? _snapshotOnRead;

    protected Options Settings { get; }

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
        var oldSnapshot = Snapshot;
        var computed = base.CreateComputed();
        if (oldSnapshot == null!) {
            // Initial value
            var firstSnapshot = Snapshot;
            using var _ = ExecutionContextExt.SuppressFlow();
            Task.Run(async () => {
                var valueOpt = Option.None<T>();
                try {
                    valueOpt = await Settings.Read().ConfigureAwait(false);
                }
                catch {
                    // Intended
                }
                if (valueOpt.IsSome(out var value)) {
                    value = await Settings.Corrector.Invoke(value).ConfigureAwait(false);
                    lock (Lock) {
                        if (Snapshot == firstSnapshot) {
                            _snapshotOnRead = firstSnapshot;
                            Set(value);
                        }
                    }
                }
            });
        }
        else {
            if (oldSnapshot == _snapshotOnRead)
                _snapshotOnRead = null; // Let's make it available for GC
            else if (computed.IsValue(out var value))
                Settings.Write(value);
        }
        return computed;
    }

    // Nested types

    public new abstract record Options : MutableState<T>.Options
    {
        public Func<T, ValueTask<T>> Corrector { get; init; } = static x => ValueTask.FromResult(x);

        internal abstract ValueTask<Option<T>> Read();
        internal abstract void Write(T value);
    }

    public record ReaderWriterOptions(
        Func<ValueTask<Option<T>>> Reader,
        Action<T> Writer
        ) : Options
    {
        internal override ValueTask<Option<T>> Read() => Reader.Invoke();
        internal override void Write(T value) => Writer.Invoke(value);
    }

    public record KvasOptions(IKvas Kvas, string Key) : Options
    {
        internal override ValueTask<Option<T>> Read() => Kvas.Get<T>(Key, CancellationToken.None);
        internal override void Write(T value) => Kvas.Set(Key, value);
    }
}
