namespace ActualChat.Performance;

public readonly struct TraceStep : IDisposable
{
    private readonly ITraceSession _trace;
    private readonly TimeSpan _startTime;
    private readonly string _startMessage;

    public TraceStep(ITraceSession trace, string startMessage)
    {
        _trace = trace;
        _startTime = trace is TraceSession traceX ? traceX.Elapsed : TimeSpan.Zero;
        _startMessage = startMessage;
    }

    public void Complete(string? message = null)
    {
        var elapsed = _trace is TraceSession traceX ? traceX.Elapsed : TimeSpan.Zero;
        var duration = elapsed - _startTime;
        var fullMessage = message.IsNullOrEmpty()
            ? _startMessage + $". Completed within {duration.TotalMilliseconds} ms"
            :  message + $" (duration: {duration.TotalMilliseconds} ms)";
        _trace.Track(fullMessage);
    }

    public void Dispose()
        => Complete();
}
