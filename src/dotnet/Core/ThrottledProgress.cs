namespace ActualChat;

public sealed class ThrottledProgress<T>(Action<T> report, TimeSpan interval) : IProgress<T>, IDisposable
{
    private readonly ThrottleWithTrailing<T> _throttle = Throttler.NewWithTrailing(interval, report);

    public void Report(T value)
        => _throttle.Throttle(value);

    public void Dispose()
        => _throttle.Dispose();
}
