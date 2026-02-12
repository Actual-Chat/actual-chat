namespace ActualChat;

/// <summary>
/// Limits action execution to at most once per specified interval.
/// </summary>
public class Throttler<T>(MomentClock clock, TimeSpan interval, Action<T> action)
{
    private readonly Lock _lock = new ();
    private Moment _lastInvokeTime = Moment.EpochStart;

    public void Throttle(T item)
    {
        var now = clock.Now;
        lock (_lock) {
            if (now - _lastInvokeTime < interval)
                return;
            _lastInvokeTime = now;
            action(item);
        }
    }
}

public class ThrottleWithTrailing<T>(MomentClock clock, TimeSpan interval, Action<T> action) : IDisposable
{
    private readonly Lock _lock = new ();
    private Moment _lastInvokeTime = Moment.EpochStart;
    private Timer? _timer;
    private bool _hasPending;
    private T? _pendingItem;
    private bool _disposed;

    public void Throttle(T item)
    {
        lock (_lock) {
            ThrowIfDisposed();

            var now = clock.Now;

            if (now - _lastInvokeTime >= interval) {
                _lastInvokeTime = now;
                action.Invoke(item);
                return;
            }

            _pendingItem = item;
            _hasPending = true;

            var delay = interval - (now - _lastInvokeTime);

            if (_timer != null)
                _timer.Change(delay, System.Threading.Timeout.InfiniteTimeSpan);
            else
                _timer = new Timer(OnTimer, null, delay,  System.Threading.Timeout.InfiniteTimeSpan);
        }
    }

    private void OnTimer(object? state)
    {
        lock (_lock)
        {
            if (_disposed)
                return;

            if (!_hasPending)
                return;

            _lastInvokeTime = DateTime.UtcNow;

            var value = _pendingItem!;
            _pendingItem = default;
            _hasPending = false;

            action.Invoke(value);
        }
    }

    private void ThrowIfDisposed()
        => ObjectDisposedException.ThrowIf(_disposed, this);

    public void Dispose()
    {
        lock (_lock) {
            if (_disposed)
                return;

            _timer?.Dispose();
            _disposed = true;
        }
    }
}

/// <summary>
/// Factory methods for creating <see cref="Throttler{T}"/> instances.
/// </summary>
public static class Throttler
{
    public static Throttler<T> New<T>(MomentClock clock, TimeSpan interval, Action<T> action)
        => new (clock, interval, action);

    public static Throttler<T> New<T>(TimeSpan interval, Action<T> action)
        => new (MomentClockSet.Default.CpuClock, interval, action);

    public static ThrottleWithTrailing<T> NewWithTrailing<T>(MomentClock clock, TimeSpan interval, Action<T> action)
        => new (clock, interval, action);

    public static ThrottleWithTrailing<T> NewWithTrailing<T>(TimeSpan interval, Action<T> action)
        => new (MomentClockSet.Default.CpuClock, interval, action);
}
