namespace ActualChat.IO;

public class CancellableDebouncer<T> : WorkerBase
{
    private readonly Channel<Context> _queue = Channel.CreateBounded<Context>(new BoundedChannelOptions(100) {
        FullMode = BoundedChannelFullMode.DropOldest,
    });

    private MomentClock Clock { get; }
    private ILogger Log { get; }
    private TimeSpan Interval { get; }
    private Func<T, CancellationToken, Task> TaskFactory { get; }

    public CancellableDebouncer(MomentClock clock, ILogger log, TimeSpan interval, Func<T, CancellationToken, Task> taskFactory)
    {
        Clock = clock;
        Log = log;
        Interval = interval;
        TaskFactory = taskFactory;
        this.Start();
    }

    protected override Task DisposeAsyncCore()
    {
        _queue.Writer.TryComplete();
        return base.DisposeAsyncCore();
    }

    public void Enqueue(T item)
    {
        if (!_queue.Writer.TryWrite(new Context(item, Clock.Now)))
            throw StandardError.Internal("Failed to enqueue debounced task.");
    }

    protected override async Task OnRun(CancellationToken cancellationToken)
    {
        Context? executing = null;
        await foreach (var context in _queue.Reader.ReadAllAsync(cancellationToken).ConfigureAwait(false))
            try {
                var clockNow = Clock.Now;
                var delay = Interval - (clockNow - context.QueuedAt);
                if (delay > TimeSpan.Zero)
                    await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
                if (_queue.Reader.Count == 0) {
                    await executing.DisposeSilentlyAsync().ConfigureAwait(false);
                    executing = context;
                    executing.Start(TaskFactory);
                }
            }
            catch(OperationCanceledException e) when (e.IsCancellationOf(cancellationToken)) { }
            catch (Exception e) {
                Log.LogError(e, "Debouncing failed");
            }
    }

    private sealed class Context(T item, Moment queuedAt) : IAsyncDisposable
    {
        private readonly object _lock = new ();
        private CancellationTokenSource? _cts;
        private volatile Task? _task;
        private bool _isDisposed;

        public Moment QueuedAt => queuedAt;

        public void Start(Func<T, CancellationToken, Task> taskFactory)
        {
            ObjectDisposedException.ThrowIf(_isDisposed, GetType());
            if (_task != null)
                return;

            lock (_lock) {
                ObjectDisposedException.ThrowIf(_isDisposed, GetType());
                if (_task != null)
                    return;

                _cts = new CancellationTokenSource();
                _task = taskFactory(item, _cts.Token);
            }
        }

        public async ValueTask DisposeAsync()
        {
            if (_isDisposed)
                return;

            lock (_lock) {
                if (_isDisposed)
                    return;

                if (_cts != null) {
                    _cts.CancelAndDisposeSilently();
                    _cts = null;
                }
                _isDisposed = true;
            }

            if (_task != null)
                await _task.WaitAsync(TimeSpan.FromSeconds(1), CancellationToken.None)
                    .SuppressExceptions()
                    .ConfigureAwait(false);
        }
    }
}

public class CancellableDebouncer(MomentClock clock, ILogger log, TimeSpan interval, Func<CancellationToken, Task> taskFactory)
    : CancellableDebouncer<Unit>(clock, log, interval, (_,ct) => taskFactory(ct))
{
    public CancellableDebouncer(TimeSpan interval, Func<CancellationToken, Task> taskFactory) : this(
        MomentClockSet.Default.CpuClock,
        StaticLog.For<CancellableDebouncer>(),
        interval,
        taskFactory)
    { }

    public void Enqueue()
        => base.Enqueue(Unit.Default);
}
