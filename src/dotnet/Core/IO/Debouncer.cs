namespace ActualChat.IO;

#pragma warning disable CA1001 // Type 'Debouncer' owns disposable field(s) '_cts' but is not disposable

public delegate Task DebouncedAction<in T>(T item, CancellationToken cancellationToken);

public class Debouncer<T>(MomentClock clock, TimeSpan interval, DebouncedAction<T> action)
{
    private CancellationTokenSource? _cts;
    private Task? _task;
    private Task _lastTask = Task.CompletedTask;
    private T _item = default!;
    private CancellationTokenSource? _taskCts;
    private object Lock => this;

    public MomentClock Clock { get; } = clock;
    public TimeSpan Interval { get; } = interval;

    public Debouncer(TimeSpan interval, DebouncedAction<T> action)
        : this(MomentClockSet.Default.CpuClock, interval, action)
    { }

    public Task WhenCompleted()
    {
        lock (Lock) {
            return _lastTask.IsCompleted
                ? Task.CompletedTask
                : _lastTask.SuppressExceptions();
        }
    }

    public void Debounce(T item, bool preferEarlierItem = false)
    {
        lock (Lock) {
            if (_task == null || !preferEarlierItem)
                _item = item;

            _cts.CancelAndDisposeSilently();
            _cts = new CancellationTokenSource();
            _lastTask = _task = WaitAndProcess(_cts.Token);
        }
    }

    public void Throttle(T item, bool preferEarlierItem = false)
    {
        lock (Lock) {
            if (_task != null) {
                if (!preferEarlierItem)
                    _item = item;
                return;
            }

            _item = item;
            _cts.CancelAndDisposeSilently();
            _cts = new CancellationTokenSource();
            _lastTask = _task = WaitAndProcess(_cts.Token);
        }
    }

    private async Task WaitAndProcess(CancellationToken cancellationToken)
    {
        try {
            await Clock.Delay(Interval, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) {
            return;
        }

        // We must do all of this before triggering action.Invoke
        T item;
        lock (Lock) {
            item = _item;
            _item = default!;
            _cts.DisposeSilently();
            _cts = null;
            _task = null;
            _taskCts.CancelAndDisposeSilently();
            _taskCts = new CancellationTokenSource();
        }

        await action.Invoke(item, _taskCts.Token).ConfigureAwait(false);
    }
}
