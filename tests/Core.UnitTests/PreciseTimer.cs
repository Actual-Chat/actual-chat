namespace ActualChat.Core.UnitTests;

public static class PreciseDelay
{
    public static (TimeSpan ActualDelay, int SpinCount) Delay(TimeSpan delay)
    {
        var start = CpuClock.Now;
        var spinCount = 0;
        while (CpuClock.Now - start < delay) {
            spinCount++;
            Thread.Yield();
        }
        return (CpuClock.Now - start, spinCount);
    }
}
