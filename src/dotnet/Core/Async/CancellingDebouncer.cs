namespace ActualChat;

[Flags]
public enum DebouncerCancellationPoints
{
    OnEnqueueNext = 1,
    OnExecuteNext = 2,
    OnDispose = 4,
}

public class CancellingDebouncer<T> : WorkerBase
{
    private readonly Channel<Item> _queue = Channel.CreateUnbounded<Item>();
    private readonly Func<T, CancellationToken, Task> _taskFactory;
    private Item? _executingItem;

    public MomentClock Clock { get; }
    public TimeSpan Interval { get; }
    public DebouncerCancellationPoints CancellationPoints { get; set; } =
        DebouncerCancellationPoints.OnExecuteNext | DebouncerCancellationPoints.OnDispose;
    public bool MustWaitForCancellation { get; set; } = true;

    public CancellingDebouncer(MomentClock clock, TimeSpan interval, Func<T, CancellationToken, Task> taskFactory)
    {
        Clock = clock;
        Interval = interval;
        _taskFactory = taskFactory;
        this.Start();
    }

    protected override Task DisposeAsyncCore()
    {
        _queue.Writer.TryComplete();
        return base.DisposeAsyncCore();
    }

    public void Enqueue(T item)
    {
        if (!_queue.Writer.TryWrite(new Item(item, Clock.Now)))
            throw StandardError.Internal("Failed to enqueue debounced task.");
    }

    protected override async Task OnRun(CancellationToken cancellationToken)
    {
        var reader = _queue.Reader;
        var whenReadyToRead = reader.WaitToReadAsync(cancellationToken).AsTask();
        while (true) {
            if (!await whenReadyToRead.ConfigureAwait(false))
                return;

            await MaybeCancel(DebouncerCancellationPoints.OnEnqueueNext).ConfigureAwait(false);

            Item item = null!;
            while (reader.TryRead(out var nextItem)) // Skipping to the very last item
                item = nextItem;

            whenReadyToRead = reader.WaitToReadAsync(cancellationToken).AsTask();
            var delay = Interval - (Clock.Now - item.QueuedAt);
            if (delay > TimeSpan.Zero) {
                var delayTask = Task.Delay(delay, cancellationToken);
                await Task.WhenAny(whenReadyToRead, delayTask).ConfigureAwait(false);
            }
            if (whenReadyToRead.IsCompleted)
                continue; // There are more items in the queue (or we're stopped), so we continue debouncing

            await MaybeCancel(DebouncerCancellationPoints.OnExecuteNext).ConfigureAwait(false);
            _executingItem = item;
            item.Start(_taskFactory);
        }
    }

    protected override Task OnStop()
        => MaybeCancel(DebouncerCancellationPoints.OnDispose);

    private Task MaybeCancel(DebouncerCancellationPoints cancellationPoint)
    {
        if ((CancellationPoints & cancellationPoint) != cancellationPoint || _executingItem is not { } executingItem)
            return Task.CompletedTask;

        _executingItem = null;
        var whenStopped = executingItem.Stop(MustWaitForCancellation);
        if (cancellationPoint != DebouncerCancellationPoints.OnDispose) // StopToken is already cancelled at this point
            whenStopped = whenStopped.WaitAsync(StopToken);
        return whenStopped;
    }

    // Nested types

    private sealed record Item(T Value, Moment QueuedAt)
    {
        private CancellationTokenSource? _stopTokenSource;
        private Task? _task;

        public void Start(Func<T, CancellationToken, Task> taskFactory)
        {
            if (_task != null)
                return;

            _stopTokenSource = new CancellationTokenSource();
            try {
                _task = taskFactory.Invoke(Value, _stopTokenSource.Token);
            }
            catch (Exception e) {
                _task = Task.FromException(e);
            }
        }

        public Task Stop(bool mustWaitForCompletion)
        {
            _stopTokenSource?.CancelAndDisposeSilently();
            _stopTokenSource = null;

            return mustWaitForCompletion && _task is { } task
                ? task.IsCompletedSuccessfully ? task : task.SuppressExceptions()
                : Task.CompletedTask;
        }
    }
}
