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
                    var data = await Settings.Read().ConfigureAwait(false);
                    if (data != null) {
                        var v = Settings.Deserializer.Invoke(data);
                        if (Settings.Corrector != null)
                            v = await Settings.Corrector.Invoke(v).ConfigureAwait(false);
                        valueOpt = v;
                    }
                }
                catch {
                    // Intended
                }
                if (valueOpt.IsSome(out var value)) {
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
            else if (computed.IsValue(out var value)) {
                var data = Settings.Serializer.Invoke(value);
                _ = Settings.Write(data);
            }
        }
        return computed;
    }

    // Nested types

    public new abstract record Options : MutableState<T>.Options
    {
        public Func<T, string> Serializer { get; init; } = static value => SystemJsonSerializer.Default.Write(value);
        public Func<string, T> Deserializer { get; init; } = static data => SystemJsonSerializer.Default.Read<T>(data);
        public Func<T, ValueTask<T>>? Corrector { get; init; }

        internal abstract ValueTask<string?> Read();
        internal abstract Task Write(string data);
    }

    public record ReaderWriterOptions(
        Func<ValueTask<string?>> Reader,
        Func<string, Task> Writer
        ) : Options
    {
        internal override ValueTask<string?> Read() => Reader.Invoke();
        internal override Task Write(string data) => Writer.Invoke(data);
    }

    public record KvasOptions(IKvas Kvas, string Key) : Options
    {
        internal override ValueTask<string?> Read() => Kvas.Get(Key);
        internal override Task Write(string data) => Kvas.Set(Key, data);
    }
}
