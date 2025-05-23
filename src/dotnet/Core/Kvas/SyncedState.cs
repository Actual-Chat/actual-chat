namespace ActualChat.Kvas;

public interface ISyncedState : IMutableState, IDisposable
{
    CancellationToken DisposeToken { get; }
    Task WhenFirstTimeRead { get; }
    Task WhenDisposed { get; }
    IComputedState ReadState { get; }
    public string? OwnOrigin { get; }

    Task WhenWritten(CancellationToken cancellationToken = default);
}

public interface ISyncedState<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] T>
    : IMutableState<T>, ISyncedState
    where T : class?
{
    new ComputedState<T> ReadState { get; }
}

public static class SyncedState
{
    public static class DefaultOptions
    {
        public static RandomTimeSpan ReadFailureDelay { get; set; } = TimeSpan.FromSeconds(1);
        public static RandomTimeSpan WriteFailureDelay { get; set; } = TimeSpan.FromSeconds(1);
        public static bool ExposeReadErrors { get; set; } = false;
        public static bool TryComputeSynchronously { get; set; } = ComputedState.DefaultOptions.TryComputeSynchronously;
        public static bool FlowExecutionContext { get; set; } = ComputedState.DefaultOptions.FlowExecutionContext;
        public static TimeSpan GracefulDisposeDelay { get; set; } = ComputedState.DefaultOptions.GracefulDisposeDelay;
    }

    public static readonly TimeSpan MaxDiscardedWriteAge = TimeSpan.FromSeconds(15);
    public static readonly string OriginPrefix = Alphabet.AlphaNumeric.Generator8.Next() + "-";
    private static long _lastId;

    public static string NextOrigin()
        => OriginPrefix + Interlocked.Increment(ref _lastId).Format();
}

public sealed class SyncedState<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] T>
    : MutableState<T>, ISyncedState<T>
    where T : class?
{
    private readonly CancellationTokenSource _disposeTokenSource;
    private readonly TaskCompletionSource _whenFirstTimeReadSource = TaskCompletionSourceExt.New();
    private bool _isReading;
    private int _writeIndex;
    private volatile Task<bool>? _writeTask;

    private Options Settings { get; }
    private MomentClock CpuClock { get; }
    [field: AllowNull, MaybeNull]
    private Queue<Expiring<T>> RecentlyWritten => field ??= new Queue<Expiring<T>>(2); // Use only inside lock (Lock)!
    private ILogger? DebugLog => CoreConstants.DebugMode.SyncedState ? Log : null;

    public CancellationToken DisposeToken { get; }
    public Task WhenFirstTimeRead => _whenFirstTimeReadSource.Task;
    public Task WhenDisposed { get; private set; } = null!;
    IComputedState ISyncedState.ReadState => ReadState;
    public ComputedState<T> ReadState { get; }
    public string? OwnOrigin { get; }

    public SyncedState(Options options, IServiceProvider services, bool initialize = true)
        : base(options, services, false)
    {
        Settings = options;
        CpuClock = services.Clocks().CpuClock;
        _disposeTokenSource = new CancellationTokenSource();
        DisposeToken = _disposeTokenSource.Token;
        if (typeof(IHasOrigin).IsAssignableFrom(typeof(T)))
            OwnOrigin = SyncedState.NextOrigin();

        var stateFactory = services.StateFactory();
        ReadState = stateFactory.NewComputed(
            new ComputedState<T>.Options() {
                ComputedOptions = options.ComputedOptions,
                UpdateDelayer = options.UpdateDelayer,
                TryComputeSynchronously = options.TryComputeSynchronously,
                FlowExecutionContext = options.FlowExecutionContext,
                GracefulDisposeDelay = options.GracefulDisposeDelay,
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

        // ReSharper disable once VirtualMemberCallInConstructor
        if (initialize)
            Initialize(options);
    }

    protected override void Initialize(IStateOptions options)
    {
        base.Initialize(options);
        WhenDisposed = BackgroundTask.Run(ReadCycle, DisposeToken);
    }

    public void Dispose()
    {
        if (DisposeToken.IsCancellationRequested)
            return;

        _disposeTokenSource.CancelAndDisposeSilently();
        _whenFirstTimeReadSource.TrySetCanceled(DisposeToken);
        ReadState.Dispose();
    }

    public async Task WhenWritten(CancellationToken cancellationToken = default)
    {
        while (true) {
            var writeTask = _writeTask;
            if (writeTask == null)
                return;

            var isWritten = await writeTask.WaitAsync(cancellationToken).ConfigureAwait(false);
            if (isWritten)
                return;
        }
    }

    // Private & protected methods

    protected override void OnSetSnapshot(StateSnapshot snapshot, StateSnapshot? prevSnapshot)
    {
        // This method is always called inside lock (Lock)

        if (_isReading || snapshot.IsInitial) {
            base.OnSetSnapshot(snapshot, prevSnapshot);
            return;
        }

        var (value, error) = snapshot.Computed.Output;
        if (error != null)
            throw StandardError.Internal($"{GetType().GetName()} cannot be set to an error.");

        if (OwnOrigin != null && value is IHasOrigin hasOrigin)
            hasOrigin.SetOrigin(OwnOrigin);

        base.OnSetSnapshot(snapshot, prevSnapshot);
        var writeIndex = ++_writeIndex;
        var prevWriteTask = _writeTask;
        _writeTask = LazyWrite((T)value!, writeIndex, prevWriteTask);
    }

    private async Task<bool> LazyWrite(T value, int writeIndex, Task? prevWriteTask)
    {
        if (prevWriteTask is { IsCompleted: false })
            await prevWriteTask.SilentAwait(false);

        var cancellationToken = DisposeToken;
        var isAdded = false;
        var isLogged = false;
        while (true) {
            try {
                lock (Lock) {
                    if (writeIndex != _writeIndex)
                        return false; // Next write is queued, so we can skip this one

                    if (!isAdded) {
                        AddRecentlyWritten(value);
                        isAdded = true;
                    }
                }
                // If we're here:
                // - prevWriteTask is completed
                // - we are either the latest write task or we block any later write task
                // - ultimately, we are the only task that can write now

                if (!isLogged) {
                    DebugLog?.LogDebug("{State}: Write = {Value}", this, value);
                    isLogged = true;
                }
                await Settings.Write(value, cancellationToken).ConfigureAwait(false);
                return true;
            }
            catch (Exception e) when (e is not OperationCanceledException) {
                var delay = Settings.WriteFailureDelay.Next();
                Log.LogError(e, $"{nameof(LazyWrite)} failed, will retry after {{Delay}}", delay.ToShortString());
                await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
            }
        }
    }

    private async Task ReadCycle()
    {
        var cancellationToken = DisposeToken;
        StateSnapshot? lastReadSnapshot = null;
        while (!cancellationToken.IsCancellationRequested) {
            try {
                if (lastReadSnapshot == ReadState.Snapshot)
                    await ReadState.Snapshot.WhenUpdated().WaitAsync(cancellationToken).ConfigureAwait(false);
                lastReadSnapshot = ReadState.Snapshot;
                if (lastReadSnapshot is not { IsInitial: false })
                    continue;

                var readResult = ((Computed<T>)lastReadSnapshot.Computed).ToTypedResult();
                lock (Lock)
                    ApplyRead(readResult);
            }
            catch (OperationCanceledException) {
                break;
            }
            catch (Exception e) {
                var delay = Settings.ReadFailureDelay.Next();
                Log.LogError(e, $"{nameof(ReadCycle)} failed, will continue after {{Delay}}", delay.ToShortString());
                await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
            }
        }
    }

    // All methods below must be called inside lock (Lock)

    private void ApplyRead(Result<T> result)
    {
        if (result.IsValue(out var value)) {
            if (MustDiscardRead(value)) {
                _whenFirstTimeReadSource.TrySetResult();
                DebugLog?.LogDebug("{State}: Read: skipping previously written value", this);
                return;
            }
        }
        else if (!Settings.ExposeReadErrors) {
            DebugLog?.LogDebug("{State}: Read: skipping an error", this);
            return;
        }

        DebugLog?.LogDebug("{State}: Read = {Result}", this, result);
        var oldIsReading = _isReading;
        _isReading = true;
        try {
            Set(result);
            _whenFirstTimeReadSource.TrySetResult();
        }
        finally {
            _isReading = oldIsReading;
        }
    }

    private bool MustDiscardRead(T value)
    {
        if (OwnOrigin == null)
            return IsRecentlyWritten(value, true);

        var hasOrigin = value as IHasOrigin; // null only if value == null
        return hasOrigin != null && OrdinalEquals(hasOrigin.Origin, OwnOrigin);
    }

    public void AddRecentlyWritten(T value)
    {
        CleanupRecentlyWritten();
        RecentlyWritten.Enqueue(new Expiring<T>(value, CpuClock.Now + SyncedState.MaxDiscardedWriteAge));
    }

    public bool IsRecentlyWritten(T value, bool mustRemove)
    {
        var now = CpuClock.Now;
        var equalityComparer = EqualityComparer<T>.Default;
        var removeCount = 0;
        var recentlyWritten = RecentlyWritten;
        foreach (var item in recentlyWritten) {
            removeCount++;
            if (item.ExpiresAt < now)
                continue;
            if (equalityComparer.Equals(item.Value, value)) {
                if (mustRemove)
                    CleanupRecentlyWritten(removeCount);
                return true;
            }
        }
        recentlyWritten.Clear();
        return false;
    }

    public void CleanupRecentlyWritten(int removeCount = 0)
    {
        var now = CpuClock.Now;
        var i = 0;
        var recentlyWritten = RecentlyWritten;
        while (recentlyWritten.TryPeek(out var item) && (i++ < removeCount || item.ExpiresAt < now))
            recentlyWritten.Dequeue();
    }

    // Nested types

    public new abstract record Options : MutableState<T>.Options
    {
        public IUpdateDelayer? UpdateDelayer { get; init; }
        public RandomTimeSpan ReadFailureDelay { get; init; } = SyncedState.DefaultOptions.ReadFailureDelay;
        public RandomTimeSpan WriteFailureDelay { get; init; } = SyncedState.DefaultOptions.WriteFailureDelay;
        public bool ExposeReadErrors { get; init; } = SyncedState.DefaultOptions.ExposeReadErrors;
        public bool TryComputeSynchronously { get; init; } = SyncedState.DefaultOptions.TryComputeSynchronously;
        public bool FlowExecutionContext { get; init; } = SyncedState.DefaultOptions.FlowExecutionContext;
        public TimeSpan GracefulDisposeDelay { get; init; } = SyncedState.DefaultOptions.GracefulDisposeDelay;

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
            var value = await Kvas.Get<T>(Key, cancellationToken).ConfigureAwait(false);
            if (value is null)
                return MissingValueFactory != null
                    ? await MissingValueFactory(cancellationToken).ConfigureAwait(false)
                    : InitialValue;

            if (Corrector != null)
                value = await Corrector.Invoke(value, cancellationToken).ConfigureAwait(false);
            return value;
        }

        internal override Task Write(T value, CancellationToken cancellationToken)
            => Kvas.Set(Key, value, cancellationToken);
    }
}
