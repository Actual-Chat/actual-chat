using ActualChat.Pooling;
using Stl.Generators;

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
    private readonly TaskCompletionSource _whenFirstTimeReadSource = TaskCompletionSourceExt.New();
    private bool _mustKeepOriginOnSet;

    private MomentClockSet Clocks { get; }
    private Options Settings { get; }
    private ILogger? DebugLog => Constants.DebugMode.SyncedState ? Log : null;

    public CancellationToken DisposeToken { get; }
    private string LocalOrigin { get; }
    public Task WhenFirstTimeRead => _whenFirstTimeReadSource.Task;
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
        Clocks = services.Clocks();
        _disposeTokenSource = new CancellationTokenSource();
        DisposeToken = _disposeTokenSource.Token;

        var stateFactory = services.StateFactory();
        LocalOrigin = RandomStringGenerator.Default.Next(8);
        ReadState = stateFactory.NewComputed(
            new ComputedState<T>.Options() {
                ComputedOptions = options.ComputedOptions,
                UpdateDelayer = options.UpdateDelayer,
                Category = StateCategories.Get(Category, nameof(ReadState)),
            },
            async (_, ct) => {
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
        if (initialize)
            Initialize(options);
 #pragma warning restore MA0056
    }

    protected override void Initialize(State<T>.Options options)
    {
        base.Initialize(options);

        WhenDisposed = BackgroundTask.Run(SyncCycle, DisposeToken);
    }

    public void Dispose()
    {
        if (DisposeToken.IsCancellationRequested)
            return;

        _disposeTokenSource.CancelAndDisposeSilently();
        if (!WhenFirstTimeRead.IsCompleted)
            _whenFirstTimeReadSource.TrySetCanceled(DisposeToken);
    }

    // Private & protected methods

    protected override void OnSetSnapshot(StateSnapshot<T> snapshot, StateSnapshot<T>? prevSnapshot)
    {
        if (snapshot.Computed.IsValue(out var value)) {
            if (value.Origin.IsNullOrEmpty() || (!_mustKeepOriginOnSet && !OrdinalEquals(value.Origin, LocalOrigin))) {
                DebugLog?.LogDebug(
                    "{State}: OnSetSnapshot: fix {Value} with Origin = {LocalOrigin}",
                    this, value, LocalOrigin);
                value.SetOrigin(LocalOrigin);
            }
        }

        base.OnSetSnapshot(snapshot, prevSnapshot);
    }

    private void SetKeepOrigin(Result<T> result)
    {
        Monitor.Enter(Lock);
        var oldMustKeepOriginOnSet = _mustKeepOriginOnSet;
        _mustKeepOriginOnSet = true;
        try {
            Set(result);
        }
        finally {
            _mustKeepOriginOnSet = oldMustKeepOriginOnSet;
            Monitor.Exit(Lock);
        }
    }

    private async Task SyncCycle()
    {
        var cancellationToken = DisposeToken;

        IStateSnapshot<T>? lastReadSnapshot = null;
        IStateSnapshot<T>? lastWrittenSnapshot = null;
        while (!cancellationToken.IsCancellationRequested) {
            try {
                var snapshot = Snapshot;
                var readyToReadTask = lastReadSnapshot != ReadState.Snapshot
                    ? Task.CompletedTask
                    : lastReadSnapshot.WhenUpdated().WaitAsync(cancellationToken);
                var readyToWriteTask = lastWrittenSnapshot != snapshot
                    ? Task.CompletedTask
                    : lastWrittenSnapshot.WhenUpdated().WaitAsync(cancellationToken);
                await Task.WhenAny(readyToReadTask, readyToWriteTask).ConfigureAwait(false);
                if (cancellationToken.IsCancellationRequested)
                    break;

                if (readyToReadTask.IsCompleted && Read(snapshot))
                    continue;
                if (readyToWriteTask.IsCompleted)
                    Write(Snapshot);
            }
            catch (Exception e) when (e is not OperationCanceledException) {
                var delay = Settings.SyncFailureDelay.Next();
                Log.LogError(e, "Failure inside SyncCycle(), will continue after {Delay}", delay.ToShortString());
                await Clocks.CpuClock.Delay(delay, cancellationToken).ConfigureAwait(false);
            }
        }
        return;

        bool Read(IStateSnapshot<T> snapshot)
        {
            lastReadSnapshot = ReadState.Snapshot;
            if (lastReadSnapshot.UpdateCount == 0)
                return false; // Initial value, i.e. nothing is read yet

            try {
                var result = lastReadSnapshot.Computed.AsResult();
                var valueOrigin = StateFactoryExt.ExternalOrigin;
                if (result.IsValue(out var value)) {
                    valueOrigin = value.Origin;
                    if (valueOrigin.IsNullOrEmpty()) {
                        valueOrigin = StateFactoryExt.ExternalOrigin;
                        value.SetOrigin(valueOrigin);
                    }
                }
                else if (!Settings.ExposeReadErrors) {
                    DebugLog?.LogDebug("{State}: Read: skipping (result is an error)", this);
                    return false;
                }

                var mustSet = !OrdinalEquals(valueOrigin, LocalOrigin); // External origin or error (null origin is external too)
                lock (Lock) {
                    mustSet &= snapshot == Snapshot; // And user didn't update it concurrently; this check requires Lock
                    if (mustSet)
                        SetKeepOrigin(result);
                }

                if (mustSet)
                    DebugLog?.LogDebug("{State}: Read = {Result}", this, result);
                else
                    DebugLog?.LogDebug("{State}: Read: skipping (local origin or changed concurrently)", this);
                return mustSet;
            }
            finally {
                if (!WhenFirstTimeRead.IsCompleted)
                    _whenFirstTimeReadSource.TrySetResult();
            }
        }

        bool Write(IStateSnapshot<T> snapshot)
        {
            lastWrittenSnapshot = snapshot;
            var computed = snapshot.Computed;
            if (snapshot.UpdateCount == 0 || !computed.IsValue(out var value))
                return false; // Initial value or error

            if (!OrdinalEquals(value.Origin, LocalOrigin)) {
                DebugLog?.LogDebug("{State}: Write: skipping (external origin)", this);
                return false;
            }

            DebugLog?.LogDebug("{State}: Write = {Value}", this, value);
            _ = ForegroundTask.Run(
                () => Settings.Write(value, cancellationToken),
                Log, "Write failed", cancellationToken);
            return true;
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
        public Func<T, CancellationToken, ValueTask<T>>? Corrector { get; init; }
        public Func<CancellationToken, ValueTask<T>>? MissingValueFactory { get; init; }

        internal override async Task<T> Read(CancellationToken cancellationToken)
        {
            var valueOpt = await Kvas.TryGet<T>(Key, cancellationToken).ConfigureAwait(false);
            if (!valueOpt.IsSome(out var value)) {
                value = MissingValueFactory != null
                    ? await MissingValueFactory(cancellationToken).ConfigureAwait(false)
                    : InitialValue;
                // Set the origin to external to make sure it won't get written
                return value.SetOrigin(StateFactoryExt.ExternalOrigin);
            }

            if (Corrector != null)
                value = await Corrector.Invoke(value, cancellationToken).ConfigureAwait(false);
            return value;
        }

        internal override Task Write(T value, CancellationToken cancellationToken)
            => Kvas.Set(Key, value, cancellationToken);
    }
}

public class SyncedStateLease<T> : MutableStateLease<T, ISyncedState<T>>, ISyncedState<T>
    where T: IHasOrigin
{
    public Task WhenFirstTimeRead => State.WhenFirstTimeRead;

    public SyncedStateLease(SharedResourcePool<Symbol, ISyncedState<T>>.Lease lease) : base(lease) { }
}
