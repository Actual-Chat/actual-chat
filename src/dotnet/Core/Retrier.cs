namespace ActualChat;

public struct Retrier
{
    public int MaxTryCount { get; }
    public RetryDelaySeq? Delays { get; }
    public int TryIndex { get; private set; } = -1;
    public TimeSpan Delay => Delays == null ? TimeSpan.Zero : Delays[TryIndex];

    public Retrier(int maxTryCount, RetryDelaySeq? delays = null)
    {
        MaxTryCount = maxTryCount;
        Delays = delays;
    }

    public override string ToString()
        => $"{GetType().Name}(#{TryIndex} / {MaxTryCount})";

    public bool Next()
        => MaxTryCount > ++TryIndex;

    public bool NextOrThrow()
        => Next() ? true
            : throw new InvalidOperationException($"Couldn't complete the operation in {MaxTryCount} attempts.");
}
