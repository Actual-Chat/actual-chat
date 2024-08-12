namespace ActualChat;

public static class ClockExt
{
    public static Timeout Timeout(this MomentClock clock, TimeSpan duration)
        => new (clock, duration);
    public static Timeout Timeout(this MomentClock clock, double duration)
        => new (clock, TimeSpan.FromSeconds(duration));

    public static Timeout Timeout(this MomentClockSet clocks, TimeSpan duration)
        => new (clocks.CpuClock, duration);
    public static Timeout Timeout(this MomentClockSet clocks, double duration)
        => new (clocks.CpuClock, TimeSpan.FromSeconds(duration));
}
