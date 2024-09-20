namespace ActualChat.IO;

public class CancellableDebouncer<T> : WorkerBase
{
    private readonly Channel<ActionContext> _queue = Channel.CreateBounded<ActionContext>(new BoundedChannelOptions(100) {
        FullMode = BoundedChannelFullMode.DropOldest,
    });

    private MomentClock Clock { get; }
    private ILogger Log { get; }
    private TimeSpan Interval { get; }
    private Func<T, CancellationToken, Task> ActionFactory { get; }

    public CancellableDebouncer(TimeSpan interval, Func<T, CancellationToken, Task> actionFactory) : this(
        MomentClockSet.Default.CpuClock,
        StaticLog.For<CancellableDebouncer<T>>(),
        interval,
        actionFactory)
    { }

    public CancellableDebouncer(MomentClock clock, ILogger log, TimeSpan interval, Func<T, CancellationToken, Task> actionFactory)
    {
        Clock = clock;
        Log = log;
        Interval = interval;
        ActionFactory = actionFactory;
        this.Start();
    }

    protected override Task DisposeAsyncCore()
    {
        _queue.Writer.TryComplete();
        return base.DisposeAsyncCore();
    }

    public void Enqueue(T item)
    {
        if (!_queue.Writer.TryWrite(new ActionContext(item, Clock.Now)))
            throw StandardError.Internal("Failed to enqueue debounced task");
    }

    protected override async Task OnRun(CancellationToken cancellationToken)
    {
        ActionContext? executing = null;
        await foreach (var context in _queue.Reader.ReadAllAsync(cancellationToken).ConfigureAwait(false))
            try {
                var clockNow = Clock.Now;
                var delay = Interval - (clockNow - context.QueuedAt);
                if (delay > TimeSpan.Zero)
                    await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
                if (_queue.Reader.Count == 0) {
                    await executing.DisposeSilentlyAsync().ConfigureAwait(false);
                    executing = context;
                    executing.Start(ActionFactory);
                }
                else {
                    Console.WriteLine("Not empty");
                }
            }
            catch (Exception e) {
                Log.LogError(e, "Debouncing failed");
            }
    }

    private sealed class ActionContext(T item, Moment queuedAt) : IAsyncDisposable
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

public class CancellableDebouncer(MomentClock clock, ILogger log, TimeSpan interval, Func<CancellationToken, Task> actionFactory)
    : CancellableDebouncer<Unit>(clock, log, interval, (_,ct) => actionFactory(ct))
{
    public CancellableDebouncer(TimeSpan interval, Func<CancellationToken, Task> actionFactory) : this(
        MomentClockSet.Default.CpuClock,
        StaticLog.For<CancellableDebouncer>(),
        interval,
        actionFactory)
    { }

    public void Enqueue()
        => base.Enqueue(Unit.Default);
}
