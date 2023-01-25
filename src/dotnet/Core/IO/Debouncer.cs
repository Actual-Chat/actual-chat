using Stl.Locking;

namespace ActualChat.IO;

public class Debouncer<T>
{
    private CancellationTokenSource? _cts;
    private Task? _task;
    private T _item = default!;
    private readonly Func<T, Task> _action;
    private readonly object _lock = new();

    public TimeSpan Interval { get; }

    public Debouncer(TimeSpan interval, Func<T, Task> action)
    {
        _action = action;
        Interval = interval;
    }

    public void Debounce(T item, bool preferEarlierItem = false)
    {
        lock (_lock) {
            if (_task != null) {
                if (!preferEarlierItem)
                    _item = item;
                _cts.CancelAndDisposeSilently();
            }
            else
                _item = item;

            _cts = new CancellationTokenSource();
            _task = WaitAndProcess(_cts.Token);
        }
    }

    public void Throttle(T item, bool preferEarlierItem = false)
    {
        lock (_lock) {
            if (_task != null) {
                if (!preferEarlierItem)
                    _item = item;
                return;
            }

            _item = item;
            _cts = new CancellationTokenSource();
            _task = WaitAndProcess(_cts.Token);
        }
    }

    private async Task WaitAndProcess(CancellationToken cancellationToken)
    {
        await Task.Delay(Interval, cancellationToken).ConfigureAwait(false);

        // We must do all of this before triggering _process.Invoke
        T item;
        lock (_lock) {
            item = _item;
            _item = default!;
            _cts.DisposeSilently();
            _cts = null;
            _task = null;
        }

        await _action.Invoke(item).ConfigureAwait(false);
    }
}
