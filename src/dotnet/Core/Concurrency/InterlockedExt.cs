namespace ActualChat.Concurrency;

public static class InterlockedExt
{
    public static void ExchangeIfGreaterThan(ref long location, long value)
    {
        var current = Interlocked.Read(ref location);
        while (current < value) {
            var previous = Interlocked.CompareExchange(ref location, value, current);
            if (previous == current || previous >= value)
                break;

            current = Interlocked.Read(ref location);
        }
    }
}
