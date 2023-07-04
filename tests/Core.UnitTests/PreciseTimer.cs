namespace ActualChat.Core.UnitTests;

public static class PreciseDelay
{
    public static TimeSpan Delay(TimeSpan delay)
    {
        var startedAt = CpuTimestamp.Now;
        var endsAt = startedAt + delay;
        while (CpuTimestamp.Now < endsAt)
            Thread.Yield();
        return CpuTimestamp.Now - startedAt;
    }
}
