using ActualChat.Pooling;

namespace ActualChat.Kvas;

public interface ISyncedState<T> : IMutableState<T>, IDisposable
{
    Task WhenFirstTimeRead { get; }
}

public sealed class SyncedState<T> : MutableState<T>, ISyncedState<T>
{
    private volatile IUpdateDelayer _updateDelayer;
    private volatile Task? _whenDisposed;
    private readonly CancellationTokenSource _disposeTokenSource;
    private StateSnapshot<T>? _lastWrittenSnapshot;
    private IComputed? _lastReadComputed;
    private ILogger? _log;

    private ILogger Log => _log ??= Services.LogFor(GetType());
    private Options Settings { get; }
    private TaskSource<Unit> WhenFirstTimeReadSource { get; }

    public Task WhenFirstTimeRead => WhenFirstTimeReadSource.Task;
    public IUpdateDelayer UpdateDelayer {
        get => _updateDelayer;
        set => _updateDelayer = value;
    }

    public CancellationToken DisposeToken { get; }
    public Task SyncCycleTask { get; private set; } = null!;
    public Task? WhenDisposed => _whenDisposed;

    public SyncedState(Options options, IServiceProvider services, bool initialize = true)
        : base(options, services, false)
    {
        Settings = options;
        _disposeTokenSource = new CancellationTokenSource();
        DisposeToken = _disposeTokenSource.Token;
        WhenFirstTimeReadSource = TaskSource.New<Unit>(true);
        _updateDelayer = options.UpdateDelayer ?? services.GetRequiredService<IUpdateDelayer>();
 #pragma warning disable MA0056
        // ReSharper disable once VirtualMemberCallInConstructor
        if (initialize) Initialize(options);
 #pragma warning restore MA0056
    }

    protected override void Initialize(State<T>.Options options)
    {
        if (SyncCycleTask != null!)
            return;
        base.Initialize(options);

        using var _ = ExecutionContextExt.SuppressFlow();
        SyncCycleTask = BackgroundTask.Run(SyncCycle, CancellationToken.None);
    }

    public void Dispose()
    {
        if (_whenDisposed != null)
            return;
        lock (Lock) {
            if (_whenDisposed != null)
                return;
            _whenDisposed = SyncCycleTask ?? Task.CompletedTask;
            _disposeTokenSource.CancelAndDisposeSilently();
        }
    }

    private async Task SyncCycle()
    {
        var cancellationToken = DisposeToken;
        try {
            await Sync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception e) when (e is not OperationCanceledException) {
            Log.LogError(e, "Failure inside SyncCycle() on the very first sync");
            throw;
        }

        while (!cancellationToken.IsCancellationRequested) {
            var cts = cancellationToken.CreateLinkedTokenSource();
            try {
                var snapshot = Snapshot;
                var readResultChangedTask = _lastReadComputed!.WhenInvalidated(cts.Token);
                var valueChangedTask = snapshot.Computed.WhenInvalidated(cts.Token);
                await Task.WhenAny(readResultChangedTask, valueChangedTask).ConfigureAwait(false);
                if (cancellationToken.IsCancellationRequested)
                    break;

                if (readResultChangedTask.IsCompleted && !valueChangedTask.IsCompleted)
                    await UpdateDelayer.Delay(snapshot.RetryCount, cancellationToken).ConfigureAwait(false);
                await Sync(cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) {
                // Will break from "while" loop later if it's due to cancellationToken cancellation
            }
            catch (Exception e) {
                Log.LogError(e, "Failure inside SyncCycle()");
            }
            finally {
                cts.CancelAndDisposeSilently();
            }
        }
    }

    private async Task Sync(CancellationToken cancellationToken)
    {
        var readResult = default(Result<T>);
        var readComputed = await Stl.Fusion.Computed.Capture(
            (Func<Task>) (async () => {
                try {
                    readResult = await Settings.Read(cancellationToken).ConfigureAwait(false);
                }
                catch (Exception e) when (e is not OperationCanceledException) {
                    readResult = Settings.ExposeReadErrors ? Result.Error<T>(e) : Settings.InitialOutput;
                    Log.LogWarning(e, "Failed to read the initial value");
                }
            })).ConfigureAwait(false);
        _lastReadComputed ??= readComputed; // It is null on the first sync

        var mustWrite = false;
        var spinWait = new SpinWait();
        while (true) {
            lock (Lock) {
                var snapshot = Snapshot;
                if (snapshot != null!) {
                    // First or subsequent read is completed at this point. So if:
                    // - we have just our first snapshot (initial value)
                    // - or last read computed has changed (it's a subsequent read)
                    if (snapshot.UpdateCount == 0 || _lastReadComputed != readComputed) {
                        // Applying read result
                        Set(readResult);
                        if (!WhenFirstTimeRead.IsCompleted) {
                            // It's the very first read
                            WhenFirstTimeReadSource.TrySetResult(default);
                            mustWrite = Settings.MustWriteInitialValue;
                        }
                        _lastReadComputed = readComputed;
                        _lastWrittenSnapshot = Snapshot;
                    }
                    else if (snapshot.UpdateCount > 0 && snapshot != _lastWrittenSnapshot) {
                        // Triggering write
                        _lastWrittenSnapshot = snapshot;
                        mustWrite = true;
                    }
                    break;
                }
                // Waiting for the first update
                spinWait.SpinOnce();
            }
        }
        if (mustWrite && _lastWrittenSnapshot!.Computed.IsValue(out var value))
            await Settings.Write(value, cancellationToken).ConfigureAwait(false);
    }

    // Nested types

    public new abstract record Options : MutableState<T>.Options
    {
        public IUpdateDelayer? UpdateDelayer { get; init; }
        public bool ExposeReadErrors { get; init; }
        public bool MustWriteInitialValue { get; init; }

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
                if (MissingValueFactory == null)
                    return InitialValue;

                var value = await MissingValueFactory(cancellationToken).ConfigureAwait(false);
                await Write(value, cancellationToken).ConfigureAwait(false);
                return value;
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
{
    public Task WhenFirstTimeRead => State.WhenFirstTimeRead;

    public SyncedStateLease(SharedResourcePool<Symbol, ISyncedState<T>>.Lease lease) : base(lease) { }
}
