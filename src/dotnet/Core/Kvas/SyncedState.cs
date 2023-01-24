using ActualChat.Pooling;

namespace ActualChat.Kvas;

public interface ISyncedState<T> : IMutableState<T>, IDisposable
    where T: IHasOrigin
{
    Task WhenFirstTimeRead { get; }
}

public sealed class SyncedState<T> : MutableState<T>, ISyncedState<T>
    where T: IHasOrigin
{
    private readonly CancellationTokenSource _disposeTokenSource;
    private bool _mustResetOrigin = true;
    private ILogger? _log;

    private ILogger Log => _log ??= Services.LogFor(GetType());
    private ILogger? DebugLog => Constants.DebugMode.SyncedState ? Log : null;
    private MomentClockSet Clocks { get; }
    private Options Settings { get; }

    public CancellationToken DisposeToken { get; }
    public Task WhenFirstTimeRead { get; }
    public Task WhenDisposed { get; private set; } = null!;
    public IComputedState<T> ReadState { get; }

    public SyncedState(Options options, IServiceProvider services, bool initialize = true)
        : base(options, services, false)
    {
        if (ReferenceEquals(options.InitialValue, null))
            throw new ArgumentOutOfRangeException(
                nameof(options),
                "SyncedState requires Settings.InitialValue != null.");

        Settings = options;
        _disposeTokenSource = new CancellationTokenSource();
        DisposeToken = _disposeTokenSource.Token;

        var stateFactory = services.StateFactory();
        Clocks = services.Clocks();
        WhenFirstTimeRead = TaskSource.New<Unit>(true).Task;
        ReadState = stateFactory.NewComputed<T>(
            new ComputedState<T>.Options() {
                ComputedOptions = options.ComputedOptions,
                UpdateDelayer = options.UpdateDelayer,
            },
            async (state, ct) => {
                try {
                    return await Settings.Read(ct).ConfigureAwait(false);
                }
                catch (Exception e) when (e is not OperationCanceledException) {
                    Log.LogWarning(e, "Read failed");
                    throw;
                }
            });

 #pragma warning disable MA0056
        // ReSharper disable once VirtualMemberCallInConstructor
        if (initialize) Initialize(options);
 #pragma warning restore MA0056
    }

    protected override void Initialize(State<T>.Options options)
    {
        base.Initialize(options);

        using var _ = ExecutionContextExt.SuppressFlow();
        WhenDisposed = BackgroundTask.Run(SyncCycle, CancellationToken.None);
    }

    public void Dispose()
    {
        if (DisposeToken.IsCancellationRequested)
            return;

        _disposeTokenSource.CancelAndDisposeSilently();
        if (!WhenFirstTimeRead.IsCompleted)
            TaskSource.For((Task<Unit>)WhenFirstTimeRead).TrySetCanceled(DisposeToken);
    }

    protected override void OnSetSnapshot(StateSnapshot<T> snapshot, StateSnapshot<T>? prevSnapshot)
    {
        if (_mustResetOrigin && snapshot.Computed.IsValue(out var value))
            value.SetOrigin("");

        base.OnSetSnapshot(snapshot, prevSnapshot);
    }

    private async Task SyncCycle()
    {
        var cancellationToken = DisposeToken;
        var origin = await Services.StateFactory().GetOriginAsync(cancellationToken).ConfigureAwait(false);

        IStateSnapshot<T>? lastReadSnapshot = null;
        IStateSnapshot<T>? lastWrittenSnapshot = null;
        while (!cancellationToken.IsCancellationRequested) {
            try {
                var snapshot = Snapshot;
                var readyToReadTask = lastReadSnapshot != ReadState.Snapshot
                    ? Task.CompletedTask
                    : lastReadSnapshot.WhenUpdated().WaitAsync(cancellationToken);
                var valueChangedTask = lastWrittenSnapshot != snapshot
                    ? Task.CompletedTask
                    : lastWrittenSnapshot.WhenInvalidated(cancellationToken);
                await Task.WhenAny(readyToReadTask, valueChangedTask).ConfigureAwait(false);
                if (cancellationToken.IsCancellationRequested)
                    break;

                if (valueChangedTask.IsCompleted)
                    TryWrite(snapshot);
                if (readyToReadTask.IsCompleted)
                    TryRead(snapshot);
            }
            catch (Exception e) when (e is not OperationCanceledException) {
                var delay = Settings.SyncFailureDelay.Next();
                Log.LogError(e, "Failure inside SyncCycle(), will continue after {Delay}", delay.ToShortString());
                await Clocks.CpuClock.Delay(delay, cancellationToken).ConfigureAwait(false);
            }
        }
        return;

        void TryRead(IStateSnapshot<T> snapshot)
        {
            lastReadSnapshot = ReadState.Snapshot;
            if (lastReadSnapshot.UpdateCount == 0)
                return; // Initial value

            try {
                var result = lastReadSnapshot.Computed.AsResult();
                var hasError = !result.IsValue(out var value);
                if (hasError && !Settings.ExposeReadErrors) {
                    DebugLog?.LogDebug("{State}: TryRead: skipping (it's an error)", this);
                    return;
                }

                var isVeryFirstRead = !WhenFirstTimeRead.IsCompleted && snapshot.UpdateCount == 0;
                var mustSet = isVeryFirstRead // Very first read
                    || !OrdinalEquals(value?.Origin, origin); // "Foreign" origin or error (null origin is "foreign" too)
                lock (Lock) {
                    mustSet &= snapshot == Snapshot; // And user didn't update it yet; this check requires Lock
                    if (mustSet) {
                        if (isVeryFirstRead && !hasError) {
                            // This value might have "own" origin, so we need to block its write
                            result = value!.WithOrigin(StateFactoryExt.ForeignOrigin);
                        }
                        _mustResetOrigin = false; // This makes sure we don't overwrite
                        try {
                            Set(result);
                        }
                        finally {
                            _mustResetOrigin = true;
                        }
                    }
                }

                if (mustSet)
                    DebugLog?.LogDebug("{State}: TryRead: read: {Result}", this, result);
                else
                    DebugLog?.LogDebug("{State}: TryRead: skipping (own origin or concurrent update)", this);
            }
            finally {
                if (!WhenFirstTimeRead.IsCompleted)
                    TaskSource.For((Task<Unit>)WhenFirstTimeRead).TrySetResult(default);
            }
        }

        void TryWrite(IStateSnapshot<T> snapshot)
        {
            lastWrittenSnapshot = snapshot;
            var computed = snapshot.Computed;
            if (snapshot.UpdateCount == 0 || !computed.IsValue(out var value))
                return; // Initial value or error

            if (!value.Origin.IsNullOrEmpty() && !OrdinalEquals(value.Origin, origin)) {
                DebugLog?.LogDebug("{State}: TryWrite: skipping (foreign origin)", this);
                return;
            }

            value = value.WithOrigin(origin);
            DebugLog?.LogDebug("{State}: TryWrite: write: {Value}", this, value);
            _ = BackgroundTask.Run(
                () => Settings.Write(value, cancellationToken),
                Log, "Write failed", cancellationToken);
        }
    }

    // Nested types

    public new abstract record Options : MutableState<T>.Options
    {
        public IUpdateDelayer? UpdateDelayer { get; init; }
        public RandomTimeSpan SyncFailureDelay { get; init; } = TimeSpan.FromSeconds(1);
        public bool ExposeReadErrors { get; init; }

        internal abstract Task<T> Read(CancellationToken cancellationToken);
        internal abstract Task Write(T value, CancellationToken cancellationToken);
    }

    public record CustomOptions(
        Func<CancellationToken, Task<T>> Reader,
        Func<T, CancellationToken, Task> Writer
        ) : Options
    {
        internal override Task<T> Read(CancellationToken cancellationToken)
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
        public Func<CancellationToken, ValueTask<T>>? MissingValueFactory { get; init; }

        internal override async Task<T> Read(CancellationToken cancellationToken)
        {
            var data = await Kvas.Get(Key, cancellationToken).ConfigureAwait(false);
            if (data == null) {
                var value = MissingValueFactory != null
                    ? await MissingValueFactory(cancellationToken).ConfigureAwait(false)
                    : InitialValue;
                // Set the origin to "foreign" to make sure it won't get written
                return value.SetOrigin(StateFactoryExt.ForeignOrigin);
            }
            else {
                var value = Serializer.Read(data);
                if (Corrector != null)
                    value = await Corrector.Invoke(value, cancellationToken).ConfigureAwait(false);
                return value;
            }
        }

        internal override Task Write(T value, CancellationToken cancellationToken)
        {
            var data = Serializer.Write(value);
            return Kvas.Set(Key, data, cancellationToken);
        }
    }
}

public class SyncedStateLease<T> : MutableStateLease<T, ISyncedState<T>>, ISyncedState<T>
    where T: IHasOrigin
{
    public Task WhenFirstTimeRead => State.WhenFirstTimeRead;

    public SyncedStateLease(SharedResourcePool<Symbol, ISyncedState<T>>.Lease lease) : base(lease) { }
}
