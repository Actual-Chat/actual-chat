namespace ActualChat.Mesh;

public sealed record RunLockedOptions(
    int MaxRelockCount,
    RetryDelaySeq RelockDelays,
    ILogger? Log = null)
{
    public static readonly RunLockedOptions Default = new(3, RetryDelaySeq.Exp(0.25, 5));
    public static readonly RunLockedOptions NoRelock = new(1);

    public RunLockedOptions(int maxRelockCount, ILogger? log = null)
        : this(maxRelockCount, Default.RelockDelays, log)
    { }

    public RunLockedOptions(ILogger? log = null)
        : this(Default.MaxRelockCount, Default.RelockDelays, log)
    { }
}
