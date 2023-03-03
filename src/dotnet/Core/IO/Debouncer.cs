namespace ActualChat.IO;

public class Debouncer<T>
{
    private CancellationTokenSource? _cts;
    private Task? _task;
    private Task _lastTask = Task.CompletedTask;
    private T _item = default!;
    private readonly Func<T, Task> _action;
    private object Lock => this;

    public IMomentClock Clock { get; }
    public TimeSpan Interval { get; }

    public Debouncer(TimeSpan interval, Func<T, Task> action)
        : this(MomentClockSet.Default.CpuClock, interval, action)
    { }

    public Debouncer(IMomentClock clock, TimeSpan interval, Func<T, Task> action)
    {
        Clock = clock;
        Interval = interval;
        _action = action;
    }

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

        // We must do all of this before triggering _process.Invoke
        T item;
        lock (Lock) {
            item = _item;
            _item = default!;
            _cts.DisposeSilently();
            _cts = null;
            _task = null;
        }

        await _action.Invoke(item).ConfigureAwait(false);
    }
}
