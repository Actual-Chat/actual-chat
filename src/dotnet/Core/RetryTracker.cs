namespace ActualChat;

public struct RetryTracker(int maxCount, RetryDelaySeq? delays = null)
{
    public Exception? LastError { get; private set; }
    public int Count { get; set; }
    public int MaxCount { get; } = maxCount;
    public RetryDelaySeq? Delays { get; } = delays;
    public TimeSpan Delay => Delays == null ? TimeSpan.Zero : Delays[Math.Max(0, Count)];

    public override string ToString()
        => $"{GetType().GetName()}(#{Count} / {MaxCount})";

    public void Reset()
    {
        LastError = null;
        Count = 0;
    }

    public bool WillRetry(Exception? error = null)
    {
        LastError = error;
        return ++Count > MaxCount;
    }
}
