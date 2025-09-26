namespace ActualChat;

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

public static class Throttler
{
    public static Throttler<T> New<T>(MomentClock clock, TimeSpan interval, Action<T> action)
        => new (clock, interval, action);

    public static Throttler<T> New<T>(TimeSpan interval, Action<T> action)
        => new (MomentClockSet.Default.CpuClock, interval, action);
}
