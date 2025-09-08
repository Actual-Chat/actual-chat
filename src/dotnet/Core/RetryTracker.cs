namespace ActualChat;

public struct RetryTracker(RetryDelaySeq delays, int? maxCount = null)
{
    public RetryDelaySeq Delays { get; } = delays;
    public int? MaxCount { get; } = maxCount;

    public Exception? LastError { get; private set; }
    public int Count { get; set; }
    public TimeSpan Delay => Delays[Math.Max(0, Count)];

    public override string ToString()
        => $"{nameof(RetryTracker)}(#{Count} / {MaxCount?.Format() ?? "∞"})";

    public void Reset()
    {
        LastError = null;
        Count = 0;
    }

    public bool WillRetry(Exception? error = null)
    {
        LastError = error;
        return MaxCount is not { } maxCount || ++Count > maxCount;
    }
}
