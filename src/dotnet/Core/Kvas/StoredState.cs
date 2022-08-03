namespace ActualChat.Kvas;

public interface IStoredState<T> : IMutableState<T>
{ }

public class StoredState<T> : MutableState<T>, IStoredState<T>
{
    private IStateSnapshot<T>? _snapshotOnRead;

    protected Symbol Key { get; }
    protected IKvas Kvas { get; }

    public StoredState(Options options, IKvas kvas, Symbol key, IServiceProvider services, bool initialize = true)
        : base(options, services, false)
    {
        Key = key;
        Kvas = kvas;
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
                var readResultOpt = Option.None<Result<T>>();
                try {
                    var valueOpt = await Kvas.Get<T>(Key, CancellationToken.None).ConfigureAwait(false);
                    if (valueOpt.IsSome(out var value))
                        readResultOpt = Option<Result<T>>.Some(value);
                }
                catch (Exception e) {
                    readResultOpt = Option<Result<T>>.Some(Result.Error<T>(e));
                }
                if (readResultOpt.IsSome(out var readResult))
                    lock (Lock) {
                        if (Snapshot == firstSnapshot) {
                            _snapshotOnRead = firstSnapshot;
                            Set(readResult);
                        }
                    }
            });
        }
        else {
            if (oldSnapshot == _snapshotOnRead)
                _snapshotOnRead = null; // Let's make it available for GC
            else if (computed.IsValue(out var value))
                Kvas.Set(Key, value);
        }
        return computed;
    }
}

public class StoredState<T, TScope> : StoredState<T>
{
    public StoredState(Options options, Symbol key, IServiceProvider services, bool initialize = true)
        : base(options, services.GetRequiredService<IKvas<TScope>>(), key, services, initialize)
    { }
}
