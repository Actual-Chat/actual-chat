using ActualChat.Pooling;

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

public interface ISyncedState<T> : IMutableState<T>, ISyncedState
{
    new IComputedState<T> ReadState { get; }
}

public static class SyncedState
{
    private static readonly string OriginPrefix = Alphabet.AlphaNumeric.Generator8.Next() + "-";
    private static long _lastId;

    public static string NextOrigin()
        => OriginPrefix + Interlocked.Increment(ref _lastId).Format();
}

public sealed class SyncedState<T> : MutableState<T>, ISyncedState<T>
{
    private readonly CancellationTokenSource _disposeTokenSource;
    private readonly TaskCompletionSource _whenFirstTimeReadSource = TaskCompletionSourceExt.New();
    private Option<T> _writingValue;
    private Option<T> _writtenValue;
    private bool _isReading;
    private int _writeIndex;
    private volatile Task<bool>? _writeTask;

    private Options Settings { get; }
    private ILogger? DebugLog => Constants.DebugMode.SyncedState ? Log : null;

    public CancellationToken DisposeToken { get; }
    public Task WhenFirstTimeRead => _whenFirstTimeReadSource.Task;
    public Task WhenDisposed { get; private set; } = null!;
    IComputedState ISyncedState.ReadState => ReadState;
    public IComputedState<T> ReadState { get; }
    public string? OwnOrigin { get; }

    public SyncedState(Options options, IServiceProvider services, bool initialize = true)
        : base(options, services, false)
    {
        Settings = options;
        _disposeTokenSource = new CancellationTokenSource();
        DisposeToken = _disposeTokenSource.Token;
        if (typeof(IHasOrigin).IsAssignableFrom(typeof(T)))
            OwnOrigin = SyncedState.NextOrigin();

        var stateFactory = services.StateFactory();
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
        WhenDisposed = BackgroundTask.Run(ReadCycle, DisposeToken);
    }

    public void Dispose()
    {
        if (DisposeToken.IsCancellationRequested)
            return;

        _disposeTokenSource.CancelAndDisposeSilently();
        if (!WhenFirstTimeRead.IsCompleted)
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

    protected override void OnSetSnapshot(StateSnapshot<T> snapshot, StateSnapshot<T>? prevSnapshot)
    {
        if (!_isReading && !snapshot.IsInitial && snapshot.Computed.IsValue(out var value)) {
            if (OwnOrigin != null && value is IHasOrigin hasOrigin)
                hasOrigin.SetOrigin(OwnOrigin);

            var writeIndex = ++_writeIndex;
            var prevWriteTask = _writeTask;
            _writeTask = Task.Run(() => Write(value, writeIndex, prevWriteTask), CancellationToken.None);
        }
        base.OnSetSnapshot(snapshot, prevSnapshot);
    }

    private async Task<bool> Write(T value, int writeIndex, Task? prevWriteTask)
    {
        if (prevWriteTask is { IsCompleted: false })
            await prevWriteTask.SilentAwait(false);

        var cancellationToken = DisposeToken;
        var isLogged = false;
        while (true) {
            try {
                lock (Lock) {
                    if (writeIndex != _writeIndex)
                        return false; // Another write task is already started, so let it do the job

                    _writingValue = value;
                }

                if (!isLogged) {
                    DebugLog?.LogDebug("{State}: Write = {Value}", this, value);
                    isLogged = true;
                }
                await Settings.Write(value, cancellationToken).ConfigureAwait(false);
                lock (Lock) {
                    if (!_writingValue.HasValue)
                        return true; // It's already read & reset

                    _writtenValue = value;
                    _writingValue = default;
                    return true;
                }
            }
            catch (Exception e) when (e is not OperationCanceledException) {
                var delay = Settings.WriteFailureDelay.Next();
                Log.LogError(e, $"{nameof(Write)} failed, will retry after {{Delay}}", delay.ToShortString());
                await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
            }
        }
    }

    private async Task ReadCycle()
    {
        var cancellationToken = DisposeToken;
        IStateSnapshot<T>? lastReadSnapshot = null;
        while (!cancellationToken.IsCancellationRequested) {
            try {
                if (lastReadSnapshot == ReadState.Snapshot)
                    await ReadState.Snapshot.WhenUpdated().WaitAsync(cancellationToken).ConfigureAwait(false);
                lastReadSnapshot = ReadState.Snapshot;
                if (lastReadSnapshot is not { IsInitial: false })
                    continue;

                var readResult = lastReadSnapshot.Computed.AsResult();
                lock (Lock)
                    ReadFromLock(readResult);
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

    private void ReadFromLock(Result<T> result)
    {
        if (result.IsValue(out var value)) {
            if (IsPreviouslyWritten(value)) {
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

    private bool IsPreviouslyWritten(T value)
    {
        if (OwnOrigin != null) {
            var hasOrigin = value as IHasOrigin; // null only if value == null
            return hasOrigin != null && OrdinalEquals(hasOrigin.Origin, OwnOrigin);
        }

        if (_writtenValue.IsSome(out var writtenValue) && EqualityComparer<T>.Default.Equals(value, writtenValue)) {
            _writtenValue = default;
            return true;
        }
        if (_writingValue.IsSome(out var writingValue) && EqualityComparer<T>.Default.Equals(value, writingValue)) {
            _writtenValue = default;
            _writingValue = default;
            return true;
        }
        return false;
    }

    // Nested types

    public new abstract record Options : MutableState<T>.Options
    {
        public IUpdateDelayer? UpdateDelayer { get; init; }
        public RandomTimeSpan ReadFailureDelay { get; init; } = TimeSpan.FromSeconds(1);
        public RandomTimeSpan WriteFailureDelay { get; init; } = TimeSpan.FromSeconds(1);
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
            if (!valueOpt.IsSome(out var value))
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

public class SyncedStateLease<T> : MutableStateLease<T, ISyncedState<T>>, ISyncedState<T>
    where T: IHasOrigin
{
    public CancellationToken DisposeToken => State.DisposeToken;
    public Task WhenFirstTimeRead => State.WhenFirstTimeRead;
    public Task WhenDisposed => State.WhenDisposed;
    IComputedState ISyncedState.ReadState => ReadState;
    public IComputedState<T> ReadState => State.ReadState;
    public string? OwnOrigin => State.OwnOrigin;

    public SyncedStateLease(SharedResourcePool<Symbol, ISyncedState<T>>.Lease lease) : base(lease) { }

    public Task WhenWritten(CancellationToken cancellationToken = default)
        => State.WhenWritten(cancellationToken);
}
