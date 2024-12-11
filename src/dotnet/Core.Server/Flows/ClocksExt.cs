namespace ActualChat.Flows;

public static class ClocksExt
{
    public static long GetMaxVersion(this MomentClockSet clocks, TimeSpan delay)
        => (clocks.CoarseCpuClock.Now - delay).EpochOffsetTicks;
}
