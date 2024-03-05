namespace ActualChat.Mesh;

public sealed record RunLockedOptions(
    int TryCount,
    RetryDelaySeq Delays,
    ILogger? Log = null)
{
    public static readonly RunLockedOptions Default = new(3, RetryDelaySeq.Exp(0.25, 5));
    public static readonly RunLockedOptions NoRetries = new(1);

    public RunLockedOptions(int tryCount, ILogger? log = null)
        : this(tryCount, Default.Delays, log)
    { }

    public RunLockedOptions(ILogger? log = null)
        : this(Default.TryCount, Default.Delays, log)
    { }
}
